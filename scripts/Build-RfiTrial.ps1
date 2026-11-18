param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root ('dist\Radio-RFI43-Trial-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Trial output already exists; choose a new directory. Existing files are never overwritten.' }
if (Test-Path -LiteralPath ($OutputDirectory + '.zip')) { throw 'Trial ZIP already exists.' }
$commit = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit.' }
$dirty = [bool](& git -C $root status --porcelain)
$buildDirectory = Join-Path $env:TEMP ('opencode\radio-rfi43-publish-' + [Guid]::NewGuid().ToString('N'))
& dotnet publish (Join-Path $root 'src\Radio.Windows') -c Release -r win-x64 --self-contained false --artifacts-path $buildDirectory -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Trial publish failed; no ZIP was created.' }
Copy-Item -LiteralPath (Join-Path $root 'docs\third-party-notices.txt') -Destination (Join-Path $OutputDirectory 'THIRD-PARTY-NOTICES.txt')
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'Start-RFI-Trial.cmd'), "@echo off`r`ncd /d `"%~dp0`"`r`nstart `"`" `"%~dp0RadioMini.exe`" --rfi-buffered --no-update-check`r`n", [Text.Encoding]::ASCII)
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'Start-Original.cmd'), "@echo off`r`ncd /d `"%~dp0`"`r`nstart `"`" `"%~dp0RadioMini.exe`" --rfi-baseline --no-update-check`r`n", [Text.Encoding]::ASCII)
$settings = @{ SchemaVersion = 2; Theme = 'dark'; Volume = 35; MyStationIds = @('rfi-monde'); PlayCounts = @{}; LastStationId = $null; StationSetupCompleted = $true }
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'radio.settings.json'), ($settings | ConvertTo-Json -Depth 5), $utf8)
$manifest = @{ kind = 'local-rfi43-trial-not-an-ota-release'; builtUtc = [DateTime]::UtcNow.ToString('O'); sourceCommit = $commit; dirtyWorkingTree = $dirty; selfContained = $false; runtime = '.NET 8 Desktop x64'; trialSwitch = '--rfi-buffered'; updatesEnabled = $false }
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'trial-build.json'), ($manifest | ConvertTo-Json -Depth 5), $utf8)
$readme = @'
RFI 43 本機試聽版（非正式 Release／非 OTA 套件）

1. 保留整個資料夾，雙擊 Start-RFI-Trial.cmd；不是直接雙擊 RadioMini.exe。
2. 預設只有 RFI，會自動開播；介面與音量控制沿用原版。
3. 對照原路徑：先從本測試版通知區選「退出」，再開 Start-Original.cmd。
   同一資料夾仍維持單一實例，沒有退出就會切回已開啟的視窗，不會改播放模式。
4. 本資料夾有自己的設定與 logs，不讀／寫正式版設定；兩個啟動方式共用這份測試設定。
   不要覆蓋正式版、不要把本目錄中的檔案複製進正式 portable 資料夾。
5. 僅在 trial 模式、已核對的 RFI Monde MP3 主／備援入口啟用背景接收與逐幀來源。
   仍用 Windows 系統解碼與輸出，不帶瀏覽器、代理伺服器、FFmpeg 或外部播放器。
6. 背景壓縮幀隊列容量小於 30 秒（64 kbps 時 payload 約 240 KB，允許逐幀變動碼率時上限約 600 KB，另有物件／接收及系統緩衝）；
   這是容量上限，不是開播倒數。收到第一個完整幀即初始化播放，沒有固定 20／30 秒預載。
   不錄音、不落地保存音訊；停止／切台丟棄隊列，重新收聽直播。
7. 既有換源／恢復門檻沒有調整。本候選尚未證明長時間穩定，可能仍會斷音或換源。
8. 請比較：斷幾次、第一次中斷前播多久、每次恢復後能連播多久；不要只比總停滯秒數。
   建議至少聽 20–60 分鐘，回報使用哪個啟動方式、開始／中斷時刻、是否切換備援。
9. logs 留在本機，含來源與錯誤；分享前請先檢閱。不會檢查／下載／套用 OTA。
10. 需已安裝 .NET 8 Desktop Runtime x64，且保留所有 DLL／runtimeconfig 檔案。
    本次沿用既有 .NET 8，並不表示已處理其支援期限問題。

檔案完整性請比對 ZIP 旁的 .sha256.txt。trial-build.json 記錄來源 commit 與工作目錄是否有未提交修改。
'@
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'READ-ME-TRIAL.txt'), $readme, $utf8)
Compress-Archive -LiteralPath $OutputDirectory -DestinationPath ($OutputDirectory + '.zip')
$hash = (Get-FileHash -LiteralPath ($OutputDirectory + '.zip') -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($OutputDirectory + '.sha256.txt'), ($hash + '  ' + [IO.Path]::GetFileName($OutputDirectory + '.zip') + "`r`n"), [Text.Encoding]::ASCII)
Write-Output ('Trial: ' + (Join-Path $OutputDirectory 'Start-RFI-Trial.cmd'))
Write-Output ('ZIP: ' + $OutputDirectory + '.zip')
Write-Output ('SHA256: ' + $hash)
