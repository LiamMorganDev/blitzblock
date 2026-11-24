param([switch]$AutoStart)

$ErrorActionPreference = 'Stop'
$exe = if ($env:BLITZBLOCK_BLITZ_EXE) { $env:BLITZBLOCK_BLITZ_EXE } else { Join-Path $env:LOCALAPPDATA 'Programs/Blitz/Blitz.exe' }

if (!$AutoStart) {
    # Blitz reuses its first instance, including after an updater relaunch.
    $regular = Get-CimInstance Win32_Process -Filter "Name='Blitz.exe'" | Where-Object {
        $_.ExecutablePath -eq $exe -and $_.CommandLine -and
        $_.CommandLine -notmatch '(?:^|\s)--(?:type=|remote-debugging-pipe\b)'
    }
    foreach ($instance in $regular) {
        Stop-Process -Id $instance.ProcessId -ErrorAction SilentlyContinue
        Wait-Process -Id $instance.ProcessId -Timeout 5 -ErrorAction SilentlyContinue
    }
}

$blitzArgs = if ($AutoStart) { '--autostart' }
$node = Join-Path $PSScriptRoot 'node.exe'
if (!(Test-Path -LiteralPath $node)) { $node = (Get-Command node.exe).Source }
& $node (Join-Path $PSScriptRoot 'blitz.cjs') @blitzArgs
exit $LASTEXITCODE
