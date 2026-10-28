# Windows portable

## 目前版本：Radio Mini v0.3.4（2026-10-28）

- 修正滑鼠在電台清單跨列移動時，整張清單與捲動條一起閃爍：列與 overlay scrollbar 統一雙緩衝繪製，hover 只更新前後兩列，同列移動不重畫。
- 離線 UI 回歸通過，包含局部重畫、捲動條拖曳、鍵盤切台及 100%～200% DPI 訊息模擬；Lite／Full 已重新封裝。未重跑線上播放或人工長時間視覺驗收。
- 雙擊根目錄 `Build-Portable.cmd` 重建，從新產生的 `dist/Radio-Portable-v0.3.4-*/Radio.exe` 啟動。`bin`／`obj` 是建置中間產出，不是交付入口；本機舊框架 `net8.0-windows` 的殘留已清除。

## 前版：Radio Mini v0.3.3（2026-10-28）

- 視窗定案為 554 × 233 DIP；間距為 item／2 DIP／bar 2 DIP／8 DIP／狀態區塊。左右區塊寬度、37 DIP 列高及字級不變。
- 新增根目錄 `Build-Portable.cmd`，雙擊依序產生 Lite／Full；封裝資料夾與 ZIP 名稱含 `v0.3.3`，執行檔版本同步更新。
- 離線 UI 與 100%～200% DPI 訊息模擬測試通過；不代表真實多螢幕或線上播放驗收。

## 前版：Radio Mini v0.3.2（2026-10-28，僅 scrollbar 調整）

- Full：`dist/Radio-Portable-Full-win-x64-20261028-110443/Radio.exe`；Lite：`dist/Radio-Portable-Lite-win-x64-20261028-110512/Radio.exe`。
- 單一修正：游標離開清單隱藏 scrollbar，拖曳中保持可見；thumb 改為 2 DIP、與 item 相隔 3 DIP，使用既有欄間空隙，上下內縮避免超出圓角。item 寬度／37 DIP 列高、550 × 233 DIP 視窗與右側面板位置不變。
- 離線 UI 回歸及 100%～200% DPI 訊息模擬通過，包含 hover／離開、清單外放開、capture 丟失、頂底界限與不誤切台。兩個 ZIP 解壓啟動／最小化／還原／退出通過，Full 確認使用包內 runtime。
- 本次不改播放、音量、metadata 或排序，未為純 UI 修正重跑線上六台測試；前版的線上驗證見下。真實多螢幕與人工視覺驗收仍待確認。

## 前版：Radio Mini v0.3.1（2026-10-28）

- Full：`dist/Radio-Portable-Full-win-x64-20261028-104214/Radio.exe`，ZIP **72.43 MiB**，解壓 **181.24 MiB**。
- Lite：`dist/Radio-Portable-Lite-win-x64-20261028-104242/Radio.exe`，ZIP **5.75 MiB**，解壓 **21.33 MiB**；需要 .NET 8 Desktop Runtime x64。
- 新增音量保存、滑桿直接點擊／拖曳、點選即開播／切台、每頁五台與細 overlay scrollbar；六台按 **91.7、92.1、96.3、98.9、99.9、107.7** 排列。列高維持原本 37 DIP，不將五列拉長；整體從 550 × 270 縮為 **550 × 233 DIP**，保持左右欄寬／字級／配色。
- 好事 989 可依官方節目表顯示節目／已提供的 DJ，標示「節目表」以區別即時播出確認。已實際取得平日 `989陽光列車／亭君`；仍不承諾歌名或臨時代班能即時取得。
- 音量與主題寫入同資料夾 `radio.settings.json`，相容舊版只有主題的設定；唯讀資料夾僅當次有效。操作完成與退出時保存，不在每次拖曳位置變化時寫檔。
- Core、原生 silent WAV 及離線 UI 測試通過；涵蓋滑鼠點擊／拖曳／滾輪、音量事件與輸出值、直接切台只啟動一次、overlay 點擊不切台、Home／End、DPI 訊息模擬下五列及滑桿命中區。
- 新 ZIP 解壓啟動／最小化／還原／退出測試通過，Full 確認載入包內 runtime；兩包皆實測鼠點音量保存為 78、切主題不清音量、重開新程序恢復為 78。這些在獨立解壓測試目錄進行，不改使用者現有設定。
- Lite 實際 UI 另經靜音驗證：直接選取中廣、再選飛碟，不按播放按鈕也會連線並收到 Playing 狀態。
- 最終兩包六台各 5 秒靜音時鐘回歸測試通過；好事 989 的短版 MainForm 另測顯示／最小化／還原各 5 秒，時鐘持續前進，縮小時 metadata loop 停止、presentation update 增量為 0，還原後顯示「節目表：989陽光列車／DJ：亭君」。短測不代表長期／人工聽音驗收。
- `dist/` 原則上只留最新版；執行中的舊版資料夾不強制關閉／刪除。更新本機資料夾時保留舊設定，發行 ZIP 不包含個人偏好。

## 前版：Radio Mini / Direction C v0.3.0（2026-10-28）

- Full：`dist/Radio-Portable-Full-win-x64-20261028-094226/Radio.exe`，ZIP **72.43 MiB**，解壓 **181.23 MiB**。
- Lite：`dist/Radio-Portable-Lite-win-x64-20261028-094254/Radio.exe`，ZIP **5.74 MiB**，解壓 **21.32 MiB**；需要 .NET 8 Desktop Runtime x64。
- 兩者均為 550 × 270 DIP 的橫向 Direction C UI，無新增大型依賴。操作與限制見 [ui-direction-c.md](ui-direction-c.md)。`dist/` 僅保留最新版兩包與對應 ZIP；2026-10-27 舊包已依使用者要求清除，原始碼保留於 Git 歷史。重建仍需相應 SDK／runtime，Git 並未保存發行執行檔。
- 最終兩包六台各 10 秒靜音時鐘短測全數通過；兩個 ZIP 均另行解壓測啟動、靜音開播、切換停止按鈕、最小化、還原與退出。Full 確認載入包內 `coreclr.dll`。

實際包的 MainForm，以中廣流行網量測每階段 10 秒（音訊時鐘均前進約 10 秒）：

| 包／狀態 | private MiB | working set MiB | 全機 CPU % |
| --- | ---: | ---: | ---: |
| Lite 顯示 | 29.04 | 95.95 | 0.000 |
| Lite 系統匣 | 28.96 | 96.81 | 0.013 |
| Lite 還原 | 29.12 | 97.75 | 0.013 |
| Full 顯示 | 28.67 | 95.31 | 0.026 |
| Full 系統匣 | 28.96 | 96.32 | 0.026 |
| Full 還原 | 28.92 | 96.73 | 0.000 |

縮小時兩包額外 metadata loop 均停止、presentation update 增量均為 0；還原後重新取得官方節目 `趙少康時間`／DJ `趙少康`。這是當時官方 API 的內容，不是介面假資料。

記憶體沒有因縮小而大幅下降，也未強制清空 working set 或 GC。CPU 數值為 12 邏輯處理器上的短時間程序取樣，0 是取樣解析度／四捨五入結果，不是絕無運算。數據不含共享音訊服務／GPU，亦不代表長期穩定與人工聽音已驗證。

首次 UI profile 因既有 adapter 將來源 prefetch 當成播放停頓而失敗，修正事件對應後重測通過；失敗 JSON 保留。第一次解壓 Lite 開播 smoke 曾逾時，後續單獨重測通過；沒有因此加入自動重連，仍需長測確認網路穩定性。

以下保留第一階段的封裝設計與歷史數據。

## 第一階段：Windows portable（2026-10-27）

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

在專案根目錄雙擊 `Build-Portable.cmd`，會依序產生 Lite 與 Full，成功後開啟 `dist/`。需要 .NET 8 SDK；失敗時會停下並保留錯誤訊息。啟動器的 PowerShell 執行政策只套用於當次程序，不修改全機設定。

```powershell
# Lite
powershell -NoProfile -File scripts/Publish-Portable.ps1

# Full，不需另裝 .NET
powershell -NoProfile -File scripts/Publish-Portable.ps1 -SelfContained
```

檔案輸出到新的 `dist/Radio-Portable-v版號-{Lite|Full}-win-x64-日期時間/` 及同名 ZIP；不覆寫或刪除舊包。`dist/` 不進 Git。若系統政策禁止 `.ps1`，不用為此調整全機政策，可由已信任的開發 PowerShell 工作階段執行腳本內容。

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
