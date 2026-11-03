param(
    [version]$Version,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'
if (-not $PSBoundParameters.ContainsKey('Version')) {
    $source = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Program.cs'))
    $match = [regex]::Match($source, '(?m)^\[assembly: AssemblyVersion\("(?<version>\d+\.\d+\.\d+\.\d+)"\)\]')
    if (-not $match.Success) { throw 'Could not read the base version from Program.cs.' }
    $Version = [version]$match.Groups['version'].Value
}
if ($null -eq $Version -or $Version.Build -lt 0 -or $Version.Revision -ne 0) {
    throw 'Package versions must have four components and a zero revision, for example 1.1.1.0.'
}
$OutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$tag = 'v' + $Version.ToString(3)
$portable = Join-Path $OutputDirectory "LidAwake-$tag-Portable.exe"
$setup = Join-Path $OutputDirectory "LidAwake-$tag-Setup.exe"
& (Join-Path $PSScriptRoot 'Build.ps1') -OutputPath $portable -Version $Version | Out-Null
Copy-Item -LiteralPath $portable -Destination $setup -Force
Get-Item -LiteralPath $portable, $setup
