[CmdletBinding()]
param([string]$NodeArchive)

$ErrorActionPreference = 'Stop'
$version = '22.23.5'
$expected = '4a191082e9706866830379d577d2cc8a50c776fcd9436a8d061b063ed0d9a0e3'
$root = $PSScriptRoot
$build = Join-Path $root 'build'
$source = Join-Path $root 'src'
$runtime = Join-Path $build 'runtime'
New-Item -ItemType Directory -Path $build -Force | Out-Null
if (!$NodeArchive) {
    $NodeArchive = Join-Path $build "node-v$version-win-x64.zip"
    if (!(Test-Path -LiteralPath $NodeArchive)) {
        Invoke-WebRequest -Uri "https://nodejs.org/dist/v$version/node-v$version-win-x64.zip" -OutFile $NodeArchive
    }
}
if ((Get-FileHash -LiteralPath $NodeArchive -Algorithm SHA256).Hash -ne $expected) {
    throw 'Unexpected Node archive hash. Check the download before rebuilding.'
}
Expand-Archive -LiteralPath $NodeArchive -DestinationPath $runtime -Force
$nodeFolder = Join-Path $runtime "node-v$version-win-x64"
$nodeExe = Join-Path $nodeFolder 'node.exe'
& $nodeExe --check (Join-Path $source 'blitz.cjs')
if ($LASTEXITCODE) { throw 'Launcher JavaScript failed the syntax check.' }
$parseErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $source 'open.ps1'), [ref]$null, [ref]$parseErrors) | Out-Null
if ($parseErrors) { throw ($parseErrors -join [Environment]::NewLine) }

$license = Join-Path $build 'Node.js LICENSE.txt'
Copy-Item -LiteralPath (Join-Path $nodeFolder 'LICENSE') -Destination $license -Force
$payload = Join-Path $build 'payload.zip'
$files = @($nodeExe, $license, (Join-Path $source 'open.ps1'), (Join-Path $source 'blitz.cjs'))
Compress-Archive -LiteralPath $files -DestinationPath $payload -Force
$id = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant()
$generated = Join-Path $build 'BlitzLauncher.cs'
(Get-Content -LiteralPath (Join-Path $source 'BlitzLauncher.cs') -Raw).Replace('__PAYLOAD_ID__', $id) |
    Set-Content -LiteralPath $generated -Encoding UTF8
$csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exe = Join-Path $build 'BlitzBlock Setup.exe'
$icon = Join-Path $source 'BlitzBlock.ico'
& $csc /nologo /target:winexe /platform:x64 /optimize+ "/out:$exe" "/win32icon:$icon" `
    /reference:Microsoft.CSharp.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Web.Extensions.dll `
    "/resource:$payload,payload.zip" $generated (Join-Path $source 'SetupWizard.cs') (Join-Path $source 'BlitzBlockUpdate.cs')
if ($LASTEXITCODE) { throw 'Setup build failed.' }
Get-Item -LiteralPath $exe
