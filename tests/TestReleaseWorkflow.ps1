param([Parameter(Mandatory = $true)][string]$TestRoot)

$ErrorActionPreference = 'Stop'
$workflow = Join-Path (Split-Path -Parent $PSScriptRoot) '.github\workflows\release.yml'
$lines = [System.IO.File]::ReadAllLines($workflow)
$publishIndex = [Array]::IndexOf($lines, '      - name: Publish release')
if ($publishIndex -lt 0) { throw 'Could not find the release publication step.' }
$runIndex = -1
for ($i = $publishIndex + 1; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -eq '        run: |') { $runIndex = $i; break }
}
if ($runIndex -lt 0) { throw 'Could not find the release publication script.' }
$scriptLines = New-Object System.Collections.Generic.List[string]
for ($i = $runIndex + 1; $i -lt $lines.Length; $i++) {
    if ($lines[$i].StartsWith('          ')) { $scriptLines.Add($lines[$i].Substring(10)) }
    elseif ([string]::IsNullOrWhiteSpace($lines[$i])) { $scriptLines.Add('') }
    else { break }
}
$publish = [scriptblock]::Create(($scriptLines -join "`n"))
$portable = Join-Path $TestRoot 'LidAwake-v1.1.2-Portable.exe'
$setup = Join-Path $TestRoot 'LidAwake-v1.1.2-Setup.exe'
[IO.File]::WriteAllText($portable, 'portable fixture')
[IO.File]::WriteAllText($setup, 'setup fixture')
$script:Commit = '1111111111111111111111111111111111111111'
$script:Portable = $portable
$script:Setup = $setup
$script:DigestPortable = 'sha256:' + (Get-FileHash -LiteralPath $portable -Algorithm SHA256).Hash.ToLowerInvariant()
$script:DigestSetup = 'sha256:' + (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()

$keys = @('GITHUB_REPOSITORY', 'GITHUB_SHA', 'RELEASE_TAG', 'RELEASE_PORTABLE', 'RELEASE_SETUP')
$previous = @{}
foreach ($key in $keys) { $previous[$key] = [Environment]::GetEnvironmentVariable($key) }
$env:GITHUB_REPOSITORY = 'Broklam/LidAwake'
$env:GITHUB_SHA = $script:Commit
$env:RELEASE_TAG = 'v1.1.2'
$env:RELEASE_PORTABLE = $portable
$env:RELEASE_SETUP = $setup

function gh {
    [void]$script:Calls.Add(($args -join ' '))
    $global:LASTEXITCODE = 0
    if ($args[0] -eq 'api' -and $args[1] -like '*matching-refs*') {
        if ($script:Mode -in @('new', 'post-fails')) { return '[]' }
        return '[{"ref":"refs/tags/v1.1.2"}]'
    }
    if ($args[0] -eq 'api' -and $args[1] -eq '-X') {
        if ($script:Mode -eq 'post-fails') { $global:LASTEXITCODE = 1 }
        return '{}'
    }
    if ($args[0] -eq 'api' -and $args[1] -like '*/commits/*') {
        if ($script:Mode -eq 'wrong-commit') { return '2222222222222222222222222222222222222222' }
        return $script:Commit
    }
    if ($args[0] -eq 'release' -and $args[1] -eq 'view') {
        if ($script:Mode -eq 'new') { $global:LASTEXITCODE = 1; return }
        $digest = if ($script:Mode -eq 'different-digest') { 'sha256:wrong' } else { $script:DigestPortable }
        $assets = @(@{ name = [IO.Path]::GetFileName($script:Portable); digest = $digest })
        if ($script:Mode -ne 'missing-asset') {
            $assets += @{ name = [IO.Path]::GetFileName($script:Setup); digest = $script:DigestSetup }
        }
        return (@{ assets = $assets } | ConvertTo-Json -Depth 5 -Compress)
    }
    if ($args[0] -eq 'release' -and $args[1] -in @('create', 'upload')) { return }
    throw ('Unexpected GitHub command: ' + ($args -join ' '))
}

function Invoke-Scenario([string]$mode, [bool]$expectFailure) {
    $script:Mode = $mode
    $script:Calls = New-Object System.Collections.ArrayList
    $failed = $false
    try { & $publish } catch { $failed = $true }
    if ($failed -ne $expectFailure) { throw "Unexpected result for release scenario $mode." }
}

try {
    Invoke-Scenario 'new' $false
    if (-not ($script:Calls -match '^api -X POST ') -or -not ($script:Calls -match '^release create .*--verify-tag')) {
        throw 'A new release did not create and verify its tag.'
    }
    Invoke-Scenario 'wrong-commit' $true
    if ($script:Calls -match '^release (create|upload) ') { throw 'A tag pointing at another commit was published.' }
    Invoke-Scenario 'existing' $false
    if ($script:Calls -match '^release (create|upload) ') { throw 'Existing matching assets were changed.' }
    Invoke-Scenario 'different-digest' $true
    if ($script:Calls -match '^release (create|upload) ') { throw 'A conflicting release asset was overwritten.' }
    Invoke-Scenario 'missing-asset' $false
    $uploads = @($script:Calls | Where-Object { $_ -match '^release upload ' })
    if ($uploads.Count -ne 1 -or $uploads[0] -notlike "*$setup*" -or $uploads[0] -like "*$portable*") {
        throw 'A release retry did not upload only the missing asset.'
    }
    Invoke-Scenario 'post-fails' $true
    if ($script:Calls -match '^release (create|upload) ') { throw 'Release proceeded after tag creation failed.' }
    Write-Output 'PASS: 6 mocked release-publication scenarios.'
}
finally {
    foreach ($key in $keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key]) }
    $global:LASTEXITCODE = 0
}
