using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace LidAwake
{
    internal static class GitHubReleases
    {
        internal const string Repository = "Broklam/LidAwake";
        internal const long MaxAssetBytes = 50L * 1024 * 1024;
        private static readonly HttpClient Client = CreateClient();

        internal static Task<Result> CheckAsync(Version installed, CancellationToken cancellationToken)
        {
            return CheckAsync(Client, installed, cancellationToken);
        }

        internal static async Task<Result> CheckAsync(HttpClient client, Version installed, CancellationToken cancellationToken)
        {
            using (HttpResponseMessage response = await client.GetAsync(
                "https://api.github.com/repos/" + Repository + "/releases/latest", cancellationToken).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new InvalidDataException("No published release was found on GitHub.");
                if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
                    throw new InvalidDataException("GitHub declined the update check. Try again later.");
                response.EnsureSuccessStatusCode();
                using (Stream content = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    return ReadRelease(content, installed);
            }
        }

        internal static Result ReadRelease(Stream content, Version installed)
        {
            var release = (Release)new DataContractJsonSerializer(typeof(Release)).ReadObject(content);
            if (release == null || string.IsNullOrWhiteSpace(release.Tag) || release.Tag.Length > 128)
                throw new InvalidDataException("GitHub did not return a usable release tag.");
            string number = release.Tag.Split('+')[0];
            if (number.StartsWith("v", StringComparison.OrdinalIgnoreCase)) number = number.Substring(1);
            Version latest;
            if (!Version.TryParse(number, out latest))
                throw new InvalidDataException("The latest release tag could not be compared: " + release.Tag + ".");
            latest = Normalize(latest);
            if (latest <= Normalize(installed)) return new Result(release.Tag, latest, null);

            Asset selected = null;
            if (release.Assets != null)
            {
                // New packages have a portable EXE; releases predating packaging used this single-EXE name.
                foreach (Asset candidate in release.Assets)
                    if (candidate != null && string.Equals(candidate.Name, "LidAwake-" + release.Tag + "-Portable.exe", StringComparison.OrdinalIgnoreCase))
                        selected = candidate;
                if (selected == null)
                    foreach (Asset candidate in release.Assets)
                        if (candidate != null && string.Equals(candidate.Name, "LidAwake-" + release.Tag + ".exe", StringComparison.OrdinalIgnoreCase))
                            selected = candidate;
            }
            if (selected == null)
                throw new InvalidDataException("Release " + release.Tag + " has no compatible Lid Awake executable.");
            Uri url;
            string expectedPath = "/" + Repository + "/releases/download/" + release.Tag + "/" + selected.Name;
            if (!Uri.TryCreate(selected.Url, UriKind.Absolute, out url) || url.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment) ||
                !string.Equals(Uri.UnescapeDataString(url.AbsolutePath), expectedPath, StringComparison.Ordinal))
                throw new InvalidDataException("The release download URL is not a valid Lid Awake GitHub asset.");
            if (selected.Size <= 0 || selected.Size > MaxAssetBytes)
                throw new InvalidDataException("The release executable has an invalid size.");
            if (!string.IsNullOrEmpty(selected.Digest) &&
                (!selected.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || selected.Digest.Length != 71 ||
                !IsHex(selected.Digest.Substring(7))))
                throw new InvalidDataException("The release executable has an invalid checksum.");
            return new Result(release.Tag, latest, selected);
        }

        internal static Task DownloadAsync(Result result, string destination, CancellationToken cancellationToken)
        {
            return DownloadAsync(Client, result, destination, cancellationToken);
        }

        internal static async Task DownloadAsync(HttpClient client, Result result, string destination, CancellationToken cancellationToken)
        {
            if (result.Download == null) throw new ArgumentException("There is no release executable to download.");
            try
            {
                using (HttpResponseMessage response = await client.GetAsync(result.Download.Url,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        byte[] buffer = new byte[81920];
                        long total = 0;
                        int count;
                        while ((count = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) != 0)
                        {
                            total += count;
                            if (total > result.Download.Size || total > MaxAssetBytes)
                                throw new InvalidDataException("The downloaded executable is larger than expected.");
                            await output.WriteAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
                        }
                        if (total != result.Download.Size)
                            throw new InvalidDataException("The downloaded executable is incomplete.");
                    }
                }
                ValidateDownloadedFile(result, destination);
            }
            catch
            {
                try { File.Delete(destination); } catch (IOException) { }
                throw;
            }
        }

        internal static void ValidateDownloadedFile(Result result, string path)
        {
            if (new FileInfo(path).Length != result.Download.Size)
                throw new InvalidDataException("The downloaded executable has an unexpected size.");
            if (!string.IsNullOrEmpty(result.Download.Digest))
            {
                using (SHA256 sha = SHA256.Create())
                using (Stream input = File.OpenRead(path))
                {
                    string actual = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
                    if (!string.Equals(actual, result.Download.Digest.Substring(7), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The downloaded executable failed its SHA-256 check.");
                }
            }
            try
            {
                Version actualVersion = AssemblyName.GetAssemblyName(path).Version;
                if (actualVersion == null || !Normalize(actualVersion).Equals(result.Version))
                    throw new InvalidDataException("The downloaded executable's version does not match the release tag.");
            }
            catch (BadImageFormatException)
            {
                throw new InvalidDataException("The downloaded file is not a valid Lid Awake executable.");
            }
        }

        internal static Version Normalize(Version version)
        {
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }

        private static bool IsHex(string value)
        {
            foreach (char c in value)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            return true;
        }

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LidAwake/" + typeof(GitHubReleases).Assembly.GetName().Version.ToString(3));
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            return client;
        }

        internal sealed class Result
        {
            internal readonly string Tag;
            internal readonly Version Version;
            internal readonly Asset Download;
            internal Result(string tag, Version version, Asset download) { Tag = tag; Version = version; Download = download; }
        }

        [DataContract]
        internal sealed class Asset
        {
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "browser_download_url")] public string Url { get; set; }
            [DataMember(Name = "size")] public long Size { get; set; }
            [DataMember(Name = "digest")] public string Digest { get; set; }
        }

        [DataContract]
        private sealed class Release
        {
            [DataMember(Name = "tag_name")] public string Tag { get; set; }
            [DataMember(Name = "assets")] public Asset[] Assets { get; set; }
        }
    }
}
