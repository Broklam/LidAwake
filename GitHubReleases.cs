using System;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LidAwake
{
    internal sealed class GitHubReleases
    {
        private static readonly Regex RepositoryPattern = new Regex(
            @"\A(?:https://github\.com/)?([A-Za-z0-9][A-Za-z0-9-]{0,38})/([A-Za-z0-9_.-]{1,104})(?:/releases(?:/latest)?)?/?\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly HttpClient Client = CreateClient();

        internal static bool TryNormalizeRepository(string input, out string repository)
        {
            repository = null;
            Match match = RepositoryPattern.Match(input.Trim());
            if (!match.Success) return false;
            string name = match.Groups[2].Value;
            if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            if (name.Length == 0 || name.Length > 100 || name == "." || name == "..") return false;
            repository = match.Groups[1].Value + "/" + name;
            return true;
        }

        internal static async Task<Result> CheckAsync(string input, Version installed, CancellationToken cancellationToken)
        {
            string repository;
            if (!TryNormalizeRepository(input, out repository))
                throw new ArgumentException("Enter a public GitHub repository as owner/repo or a github.com URL.");
            using (HttpResponseMessage response = await Client.GetAsync(
                "https://api.github.com/repos/" + repository + "/releases/latest", cancellationToken).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return new Result("No public release found. Check the repository name and whether it has a published release.");
                if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
                    return new Result("GitHub declined the update check. Try again later.");
                if (!response.IsSuccessStatusCode)
                    return new Result("GitHub returned HTTP " + (int)response.StatusCode + ". Try again later.");
                using (var content = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var release = (Release)new DataContractJsonSerializer(typeof(Release)).ReadObject(content);
                    if (release == null || string.IsNullOrWhiteSpace(release.Tag) || release.Tag.Length > 128)
                        return new Result("GitHub did not return a usable release tag.");
                    Version latest;
                    string tag = release.Tag;
                    string number = tag.Split('+')[0];
                    if (number.StartsWith("v", StringComparison.OrdinalIgnoreCase)) number = number.Substring(1);
                    var page = new Uri("https://github.com/" + repository + "/releases/latest");
                    if (!Version.TryParse(number, out latest))
                        return new Result("Latest release: " + tag + ". Its tag could not be compared to this version.", page);
                    if (Normalize(latest) > Normalize(installed))
                        return new Result("Update available: " + tag + ".", page);
                    return new Result("You're up to date. Latest release: " + tag + ".");
                }
            }
        }

        private static Version Normalize(Version version)
        {
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(12),
                MaxResponseContentBufferSize = 4 * 1024 * 1024
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LidAwake/" + typeof(GitHubReleases).Assembly.GetName().Version.ToString(3));
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
            return client;
        }

        internal sealed class Result
        {
            internal readonly string Message;
            internal readonly Uri Page;
            internal Result(string message, Uri page = null) { Message = message; Page = page; }
        }

        [DataContract]
        private sealed class Release
        {
            [DataMember(Name = "tag_name")]
            public string Tag { get; set; }
        }
    }
}
