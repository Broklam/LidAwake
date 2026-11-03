param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'LidAwake.exe'),
    [version]$Version
)

$ErrorActionPreference = 'Stop'
$OutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework 4 compiler was not found.' }

$versionedProgram = $null
$programPath = 'Program.cs'
Push-Location -LiteralPath $PSScriptRoot
try {
    if ($PSBoundParameters.ContainsKey('Version')) {
        if ($null -eq $Version -or $Version.Build -lt 0 -or $Version.Revision -lt 0) {
            throw 'Version must have four numeric components, such as 1.1.1.0.'
        }
        if (@($Version.Major, $Version.Minor, $Version.Build, $Version.Revision) | Where-Object { $_ -gt 65534 }) {
            throw 'Each version component must be at most 65534.'
        }
        $source = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Program.cs'))
        foreach ($attribute in @('AssemblyVersion', 'AssemblyFileVersion')) {
            $pattern = '(\[assembly:\s*' + $attribute + '\(")[^"]+("\)\])'
            if ([regex]::Matches($source, $pattern).Count -ne 1) {
                throw "Expected exactly one $attribute attribute in Program.cs."
            }
            $source = [regex]::Replace($source, $pattern, ('${1}' + $Version.ToString(4) + '${2}'))
        }
        $versionedProgram = Join-Path ([System.IO.Path]::GetTempPath()) ("LidAwake-" + [guid]::NewGuid().ToString('N') + '.cs')
        [System.IO.File]::WriteAllText($versionedProgram, $source, (New-Object System.Text.UTF8Encoding $false))
        $programPath = $versionedProgram
    }

    $arguments = @(
        '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/warnaserror+', '/codepage:65001',
        "/out:$OutputPath", '/win32icon:assets/lid-off.ico', '/win32manifest:app.manifest',
        '/resource:assets/lid-off.ico,lid-off.ico', '/resource:assets/lid-on.ico,lid-on.ico',
        '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Net.Http.dll',
        '/reference:System.Runtime.Serialization.dll',
        $programPath, 'LidOverride.cs', 'WindowsPower.cs', 'AppSettings.cs', 'GitHubReleases.cs',
        'Installer.cs', 'LaunchChoice.cs', 'Updates.cs', 'SettingsWindow.cs'
    )
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
    Get-Item -LiteralPath $OutputPath
}
finally {
    Pop-Location
    if ($versionedProgram -and (Test-Path -LiteralPath $versionedProgram)) {
        Remove-Item -LiteralPath $versionedProgram
    }
}
