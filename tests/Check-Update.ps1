param([Parameter(Mandatory)][string]$Setup)

$ErrorActionPreference = 'Stop'
$type = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $Setup).Path).GetType('BlitzBlockUpdate')
$flags = [Reflection.BindingFlags]'NonPublic,Static'
$find = $type.GetMethod('FindUpdate', $flags)
$digest = $type.GetMethod('MatchesDigest', $flags)
$hash = (Get-FileHash -LiteralPath $Setup -Algorithm SHA256).Hash.ToLowerInvariant()
$current = [version]'1.0.0.0'
$asset = @{
    name = 'BlitzBlock.Setup.exe'
    browser_download_url = 'https://github.com/LiamMorganDev/blitzblock/releases/download/v1.0.1/BlitzBlock.Setup.exe'
    size = (Get-Item -LiteralPath $Setup).Length
    digest = "sha256:$hash"
}
$release = @{ tag_name = 'v1.0.1'; prerelease = $false; draft = $false; assets = @($asset) }

function Assert-Update([bool]$Expected) {
    $json = $release | ConvertTo-Json -Depth 5 -Compress
    $arguments = [object[]]@($json.PSObject.BaseObject, $current, $null, $null, $null)
    if ($find.Invoke($null, $arguments) -ne $Expected) { throw "Unexpected update eligibility for $($arguments[0])" }
    if ($Expected -and ($arguments[2] -ne [version]'1.0.1.0' -or $arguments[4] -ne $hash)) {
        throw 'Version or SHA-256 did not match the release.'
    }
}
Assert-Update $true
$release.prerelease = $true
Assert-Update $false
$release.prerelease = $false
$release.tag_name = 'v1'
Assert-Update $false
$release.tag_name = 'v1.0.1'
$asset.digest = ''
Assert-Update $false
$asset.digest = "sha256:$hash"
$asset.browser_download_url = 'https://example.org/BlitzBlock.Setup.exe'
Assert-Update $false
if (!$digest.Invoke($null, @($Setup, $hash)) -or $digest.Invoke($null, @($Setup, ('0' * 64)))) {
    throw 'SHA-256 verification did not accept the exact file and reject a mismatch.'
}
'Update eligibility and digest verification passed.'
