param([Parameter(Mandatory=$true)][string]$Work,
      [Parameter(Mandatory=$true)][string]$Destination,
      [Parameter(Mandatory=$true)][int]$ParentId,
      [switch]$NoRestart)

$ErrorActionPreference = 'Stop'
$changed = [System.Collections.Generic.List[object]]::new()
$backup = Join-Path $Work 'backup'
$payload = Join-Path $Work 'payload'
$log = Join-Path $Work 'result.txt'
$playerExited = $false
try {
    $parent = Get-Process -Id $ParentId -ErrorAction Stop
    # Hold the process handle before signalling readiness, avoiding PID reuse.
    $null = $parent.Handle
    if (!(Test-Path -LiteralPath (Join-Path $payload 'RadioMini.exe'))) { throw 'Missing payload.' }
    $files = @(Get-ChildItem -LiteralPath $payload -Recurse -File)
    foreach ($file in $files) {
        if ($file.Name -ieq 'radio.settings.json') { throw 'Settings must not be updated.' }
        $relative = $file.FullName.Substring($payload.Length).TrimStart('\')
        $target = Join-Path $Destination $relative
        # Do not follow junctions/symlinks in a portable folder.
        $check = $target
        while ($check) {
            if ((Test-Path -LiteralPath $check) -and ((Get-Item -LiteralPath $check -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw 'Update destination contains a link.'
            }
            $check = Split-Path -Parent $check
        }
    }
    Set-Content -LiteralPath (Join-Path $Work 'ready') -Value 'ready'
    if (!$parent.WaitForExit(60000)) { throw 'Player did not exit; update cancelled.' }
    $playerExited = $true
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($payload.Length).TrimStart('\')
        $target = Join-Path $Destination $relative
        $saved = Join-Path $backup $relative
        $existed = Test-Path -LiteralPath $target
        if ($existed) {
            New-Item -ItemType Directory -Path (Split-Path -Parent $saved) -Force | Out-Null
            Copy-Item -LiteralPath $target -Destination $saved
        }
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        $changed.Add(@{ Target = $target; Saved = $saved; Existed = $existed })
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
    Set-Content -LiteralPath $log -Value 'Update completed.'
    if (!$NoRestart) { Start-Process -FilePath (Join-Path $Destination 'RadioMini.exe') -WorkingDirectory $Destination -ErrorAction Stop | Out-Null }
} catch {
    $details = $_.Exception.Message
    for ($i = $changed.Count - 1; $i -ge 0; $i--) {
        $item = $changed[$i]
        try {
            if ($item.Existed) { Copy-Item -LiteralPath $item.Saved -Destination $item.Target -Force }
            else { Remove-Item -LiteralPath $item.Target -Force -ErrorAction Stop }
        } catch { $details += "`r`nRollback: " + $_.Exception.Message }
    }
    Set-Content -LiteralPath $log -Value $details
    if (!$NoRestart -and $playerExited) {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show("更新失敗，已嘗試還原舊版。請重新開啟播放器。`r`n記錄：$log", 'Radio Mini') | Out-Null
    }
    exit 1
}
# Cleanup cannot turn a successful restart into a rollback of running binaries.
# Remove only this update's own files; retain the small diagnostic log.
Remove-Item -LiteralPath $payload -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue }
Remove-Item -LiteralPath (Join-Path $Work 'update.zip') -Force -ErrorAction SilentlyContinue
