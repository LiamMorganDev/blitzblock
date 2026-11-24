param([Parameter(Mandatory)][string]$Setup)

$ErrorActionPreference = 'Stop'
$setupPath = (Resolve-Path -LiteralPath $Setup).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N'))
$mutex = New-Object System.Threading.Mutex($false, 'Local\LiamMorganDev.BlitzBlock')
$ownsMutex = $false
try {
    $ownsMutex = $mutex.WaitOne(0)
    if (!$ownsMutex) { throw 'Quit BlitzBlock before running the cold-start contention check.' }
    New-Item -ItemType Directory -Path $root | Out-Null
    $copy = Join-Path $root 'BlitzBlock.exe'
    Copy-Item -LiteralPath $setupPath -Destination $copy
    Join-Path $root 'missing-blitz' | Set-Content -LiteralPath (Join-Path $root 'blitz-location.txt')

    $duplicate = Start-Process -FilePath $copy -ArgumentList '--skip-update' -WindowStyle Hidden -PassThru
    try {
        if (!$duplicate.WaitForExit(5000) -or $duplicate.ExitCode -ne 0) {
            throw 'The second launcher did not exit cleanly while another session owned the lock.'
        }
    } finally {
        if (!$duplicate.HasExited) { $duplicate.Kill(); $duplicate.WaitForExit() }
        $duplicate.Dispose()
    }
    'Single-instance contention check passed.'
} finally {
    if ($ownsMutex) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
    foreach ($name in @('BlitzBlock.exe', 'blitz-location.txt')) {
        Remove-Item -LiteralPath (Join-Path $root $name) -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $root) { [IO.Directory]::Delete($root) }
}
