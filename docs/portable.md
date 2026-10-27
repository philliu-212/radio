# 第一階段：Windows portable

## 兩種封裝

| 版本 | .NET 需求 | 使用方式 |
| --- | --- | --- |
| Lite | 目標電腦需有 .NET 8 Desktop Runtime x64 | 解壓縮整個資料夾，雙擊 `Radio.exe` |
| Full | 內含 .NET runtime，不需要另裝 .NET 或 SDK | 同上；必須保留整個資料夾 |

兩者功能相同，不包含 mpv 或 FFmpeg，不需要命令列啟動。Full 是資料夾式 self-contained，沒有為了單一 exe 把 runtime 藏在自解壓縮封裝內。兩者仍依賴 Windows 的媒體元件；Windows N 版本需要 Media Feature Pack。

目前以現有 .NET 8 SDK 建置。Full 的 .NET/Core、Windows Desktop runtime 及 apphost 固定為官方 release metadata 查得的 **8.0.33**，不把這台原本較舊的 8.0.6 runtime 當成新版發行包。版本與來源見 `scripts/runtime-package.json`。SDK 8.0.301 的框架版本解析需要逐一指定真正的 runtime，避免把 Windows SDK targeting pack 誤當成 8.x runtime。

**.NET 8 支援將於 2026-11-10 結束**；這些是驗證包，長期發行前需升級至受支援的 LTS runtime。Lite 在本機使用哪個 patch 版本取決於安裝環境，與 Full 的內含版本不一定相同。

## 為什麼 Lite 不再是 0.18 MiB

0.18 MiB 是最初只用直接 Media Engine 的實驗包，五台可播，但 Hit FM 持續回報 `NS_E_BAD_REQUEST / 0xC00D2EE3`，不能當成完整六台版本。

目前用我們自己寫的 `SystemAudioPlayer`，依已知來源類型選擇系統 API：

- 好事、KISS、POP、飛碟、中廣：Windows Media Engine 的 `MF_MEDIA_ENGINE_AUDIOONLY` 模式。
- Hit FM：保留原有 Windows MediaPlayer／AdaptiveMediaSource 相容路徑。
- 這是確定的相容性規則，不是在失敗時自動重試或改用外部播放器。
- KISS 回報 `hasAudio=true`、`hasVideo=false`；不需 mpv 才能避免影像負擔。

因此需要帶 Windows API 的 .NET 投影 DLL，約 21 MiB，而不是 mpv 的約 115.5 MiB 執行檔。Lite／Full 的差異主要是**磁碟封裝與 runtime 相依**，不能直接推導成同等的記憶體差異。

## 重新建置

```powershell
# Lite
powershell -NoProfile -File scripts/Publish-Portable.ps1

# Full，不需另裝 .NET
powershell -NoProfile -File scripts/Publish-Portable.ps1 -SelfContained
```

檔案輸出到新的 `dist/Radio-Portable-{Lite|Full}-win-x64-日期時間/` 及同名 ZIP；不覆寫或刪除舊包。`dist/` 不進 Git。若系統政策禁止 `.ps1`，不用為此調整全機政策，可由已信任的開發 PowerShell 工作階段執行腳本內容。

測試封裝需直接執行該包的 `Radio.exe`，而不是用 `dotnet run`，否則會量到開發環境而非真正交付的 runtime。

```powershell
powershell -NoProfile -File scripts/Smoke-Test.ps1 -ExecutablePath C:\path\Radio.exe -Playback
# Full 額外確認實際載入包內的 coreclr.dll
powershell -NoProfile -File scripts/Smoke-Test.ps1 -ExecutablePath C:\path\Radio.exe -ExpectLocalRuntime
```

自動化播放實驗保持靜音，不改系統音量，不會自動播放。程式碼與封裝不會儲存串流 token。

## 此階段尚未做完的項目

- 深色／淺色模式與 UI 重整，列為第二階段，不把舊介面當成最終版。
- 顯示、縮到系統匣、還原的分開量測及背景 UI 更新控制，列為第三階段。
- 人工聽音、長時間播放、斷網／睡眠喚醒測試。
- 以 Windows 內建框架或完全原生程式繼續壓縮免 runtime 安裝版本，需另做移植評估。

## 已交付與實測（2026-10-27）

| 包 | ZIP | 解壓後檔案 | runtime |
| --- | ---: | ---: | --- |
| Lite | 5.72 MiB | 21.28 MiB | 本機測試使用已安裝的 8.0.6 |
| Full | 72.41 MiB | 181.19 MiB | 包內 .NET / Windows Desktop 8.0.33 |

最終包兩者的六台各 15 秒靜音短測均通過，播放時鐘均持續前進；不包含人工聽音與長時間穩定驗證。每個包皆直接執行自己的 `Radio.exe`，不是從開發 SDK 啟動。

| 電台 | Lite private MiB | Full private MiB |
| --- | ---: | ---: |
| 好事989 | 22.16 | 22.07 |
| KISS | 26.13 | 25.72 |
| POP | 26.31 | 25.96 |
| 飛碟 | 25.36 | 26.28 |
| 中廣流行網 | 29.02 | 29.21 |
| Hit FM 北部 | 30.92 | 30.22 |

KISS 的 working set 分別約 82.56／83.75 MiB；private bytes 與 working set 意義不同，不能把較小的一個當成「全部實體 RAM」。兩者全機 CPU 的短測平均均低於 0.05%（12 個邏輯處理器），這類極小數字受計時解析度影響，不適合精準排名。

這些效能數據在隱藏的實驗視窗取得，**不是顯示／最小化／還原的對照結果**。該對照仍在第三階段。不同 runtime 及先後測試也不能推論兩包數字永遠相同。

另外已由 ZIP 解壓到獨立測試位置，驗證雙擊式啟動、六台清單、靜音 UI 開播、停止、縮到系統匣、關窗退出；Full 額外確認 `coreclr.dll` 實際來自包內。離線 WAV 另驗證原生音訊音量讀回、重複播放／停止、取消、錯誤來源與 Dispose。

第一次 Full 試驗曾因飛碟官網連線失敗而未全數通過；現改為重用經官方頁面核對的固定公開串流，不把下載首頁當成每次播放的前置條件。初次失敗與最終成功的本機 JSON 都保留在 `artifacts/`，沒有隱藏或覆寫失敗紀錄。
