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
function Assert-NoLink([string]$Path) {
    while ($Path) {
        if ((Test-Path -LiteralPath $Path) -and ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Update path contains a link.'
        }
        $Path = Split-Path -Parent $Path
    }
}
function Assert-File([string]$Path, $Entry) {
    Assert-NoLink $Path
    if (!(Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -ne $Entry.size -or
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ine $Entry.sha256) {
        throw "File changed or corrupt: $($Entry.path)"
    }
}
try {
    $parent = Get-Process -Id $ParentId -ErrorAction Stop
    $null = $parent.Handle
    $plan = Get-Content -LiteralPath (Join-Path $Work 'plan.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $files = @($plan.files)
    $seen = @{}
    if ($files.Count -lt 5 -or $files.Count -gt 10001) { throw 'Invalid update plan.' }
    foreach ($file in $files) {
        $parts = $file.path.Split('/')
        if (!$file.path -or $file.path.Length -gt 240 -or $seen.ContainsKey($file.path) -or
            $file.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or $file.size -lt 0 -or
            @($parts | Where-Object { $_ -notmatch '^[A-Za-z0-9_.-]+$' -or $_ -in @('.', '..', 'radio.settings.json') -or
                $_.EndsWith('.') -or $_ -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)' }).Count) { throw 'Unsafe update plan.' }
        $seen[$file.path] = $true
        $target = Join-Path $Destination $file.path
        Assert-NoLink $target
        if ($file.expectMissing -and (Test-Path -LiteralPath $target)) { throw "New file now conflicts with local data: $($file.path)" }
        if ($file.delete) {
            if ($file.path -ieq 'radio.update-manifest.json') { throw 'Cannot delete installed manifest.' }
            if (Test-Path -LiteralPath $target) { Assert-File $target $file }
        } elseif ($file.changed) { Assert-File (Join-Path $payload $file.path) $file }
        # Unchanged files were hashed by the app; revalidate them after exit,
        # not twice before exit. Audio continues during preparation.
    }
    foreach ($required in @('RadioMini.exe', 'RadioMini.dll', 'RadioMini.deps.json', 'RadioMini.runtimeconfig.json', 'Radio.Core.dll', 'radio.update-manifest.json')) {
        if (!@($files | Where-Object { $_.path -ieq $required -and !$_.delete }).Count) { throw 'Incomplete update plan.' }
    }
    Set-Content -LiteralPath (Join-Path $Work 'ready') -Value 'ready'
    if (!$parent.WaitForExit(60000)) { throw 'Player did not exit; update cancelled.' }
    $playerExited = $true
    # Validate the complete target version again after exit, including unchanged
    # local files and old hashes of deletions. Do not install a partial version.
    foreach ($file in $files) {
        $target = Join-Path $Destination $file.path
        Assert-NoLink $target
        if ($file.expectMissing -and (Test-Path -LiteralPath $target)) { throw "New file now conflicts with local data: $($file.path)" }
        if ($file.delete) {
            if (Test-Path -LiteralPath $target) { Assert-File $target $file }
        } elseif ($file.changed) { Assert-File (Join-Path $payload $file.path) $file }
        else { Assert-File $target $file }
    }
    foreach ($file in @($files | Where-Object { $_.changed -or $_.delete })) {
        $target = Join-Path $Destination $file.path
        $saved = Join-Path $backup $file.path
        $existed = Test-Path -LiteralPath $target
        if ($existed) {
            New-Item -ItemType Directory -Path (Split-Path -Parent $saved) -Force | Out-Null
            Copy-Item -LiteralPath $target -Destination $saved
        }
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        $changed.Add(@{ Target = $target; Saved = $saved; Existed = $existed })
        if ($file.delete) {
            if ($existed) { Remove-Item -LiteralPath $target -Force }
        } else { Copy-Item -LiteralPath (Join-Path $payload $file.path) -Destination $target -Force }
    }
    foreach ($file in @($files | Where-Object { $_.changed -and !$_.delete })) { Assert-File (Join-Path $Destination $file.path) $file }
    Set-Content -LiteralPath $log -Value 'Update completed.'
    if (!$NoRestart) { Start-Process -FilePath (Join-Path $Destination 'RadioMini.exe') -WorkingDirectory $Destination -ErrorAction Stop | Out-Null }
} catch {
    $details = $_.Exception.Message
    for ($i = $changed.Count - 1; $i -ge 0; $i--) {
        $item = $changed[$i]
        try {
            if ($item.Existed) { Copy-Item -LiteralPath $item.Saved -Destination $item.Target -Force }
            elseif (Test-Path -LiteralPath $item.Target) { Remove-Item -LiteralPath $item.Target -Force -ErrorAction Stop }
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
Remove-Item -LiteralPath $payload -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue }
