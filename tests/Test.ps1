$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $project 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$dist = (Resolve-Path -LiteralPath $dist).Path
$testRoot = Join-Path $dist ('test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
try {
    $fixture = Join-Path $testRoot 'fixture.exe'
    $broken = Join-Path $testRoot 'broken.exe'
    $original = Join-Path $testRoot 'original.exe'
    $legacy = Join-Path $testRoot 'legacy.exe'
    $runner = Join-Path $testRoot 'test-runner.exe'
    & $csc /nologo /target:winexe /warnaserror+ "/out:$fixture" (Join-Path $PSScriptRoot 'FakeUpdatedApp.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Could not build healthy fixture.' }
    & $csc /nologo /target:winexe /warnaserror+ /define:BROKEN "/out:$broken" (Join-Path $PSScriptRoot 'FakeUpdatedApp.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Could not build broken fixture.' }
    & $csc /nologo /target:winexe /warnaserror+ /define:OLD "/out:$original" (Join-Path $PSScriptRoot 'FakeUpdatedApp.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Could not build original fixture.' }
    & $csc /nologo /target:winexe /warnaserror+ /define:LEGACY "/out:$legacy" (Join-Path $PSScriptRoot 'FakeUpdatedApp.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Could not build legacy fixture.' }
    $sources = @('Program.cs', 'LidOverride.cs', 'WindowsPower.cs', 'AppSettings.cs', 'GitHubReleases.cs',
        'Installer.cs', 'LaunchChoice.cs', 'Updates.cs', 'SettingsWindow.cs') | ForEach-Object { Join-Path $project $_ }
    $arguments = @('/nologo', '/target:exe', '/warnaserror+', '/main:ReleaseTests', '/codepage:65001',
        "/out:$runner", '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
        '/reference:System.Net.Http.dll', '/reference:System.Runtime.Serialization.dll',
        (Join-Path $PSScriptRoot 'ReleaseTests.cs')) + $sources
    & $csc @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Could not build test runner.' }
    & $runner $testRoot $fixture $broken $original $legacy
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    & (Join-Path $PSScriptRoot 'TestReleaseWorkflow.ps1') -TestRoot $testRoot
}
finally {
    $resolved = [System.IO.Path]::GetFullPath($testRoot)
    if (-not $resolved.StartsWith($dist + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove test directory outside dist: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
