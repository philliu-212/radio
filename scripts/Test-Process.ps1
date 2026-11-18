function Invoke-RadioTestProcess {
    param(
        [string]$FilePath, [string[]]$Arguments, [string]$WorkingDirectory,
        [string]$LogBase, [int]$TimeoutSeconds, [switch]$ShowOutput
    )
    # Windows PowerShell 5 Start-Process takes a command-line string, not argv.
    $quoted = foreach ($argument in $Arguments) {
        '"' + (($argument -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"'
    }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $FilePath -ArgumentList ($quoted -join ' ') `
        -WorkingDirectory $WorkingDirectory -NoNewWindow -PassThru `
        -RedirectStandardOutput ($LogBase + '.stdout.log') -RedirectStandardError ($LogBase + '.stderr.log')
    try {
        # Retain the native handle before WaitForExit; Windows PowerShell 5 may
        # otherwise lose ExitCode when the process has already been reaped.
        $null = $process.Handle
        $timedOut = !$process.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) {
            # Only this runner-owned process and its descendants.
            & taskkill.exe /PID $process.Id /T /F | Out-Host
            if (!$process.WaitForExit(5000)) { throw "Could not terminate timed-out process $($process.Id)." }
        }
        else { $process.WaitForExit() }
        $exitCode = if ($timedOut) { 124 } else { $process.ExitCode }
        if ($ShowOutput -or $exitCode -ne 0) {
            foreach ($suffix in @('.stdout.log','.stderr.log')) {
                Get-Content -LiteralPath ($LogBase + $suffix) -Encoding UTF8 | Out-Host
            }
        }
        [pscustomobject]@{
            ExitCode = $exitCode
            Status = $(if ($timedOut) { 'Timeout' } elseif ($exitCode -eq 0) { 'Passed' } else { 'Failed' })
            Seconds = [Math]::Round($clock.Elapsed.TotalSeconds, 3)
        }
    }
    finally { $process.Dispose() }
}
