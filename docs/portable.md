# Windows portable 封裝指引

此文件只記錄現行封裝方式，不再混放歷代 UI 草稿、效能表或「尚未實作」的舊計畫。歷史驗證結果請查 Git／Issue／PR；本文件不是新版本已完成線上或人工驗收的宣告。

## Lite 與 Full

| 包 | 相依條件 | 啟動 |
| --- | --- | --- |
| Lite | Windows 10 2004 以上、.NET 8 Desktop Runtime x64 | 解壓完整資料夾，雙擊 `RadioMini.exe` |
| Full | 同上 Windows，包內附 .NET runtime，不需另裝 .NET 或 SDK | 同上，必須保留整個資料夾 |

兩包使用同一個 `src/Radio.Windows` 專案，功能相同，不包含 mpv／FFmpeg。仍依賴 Windows 媒體元件；Windows N 版本需 Media Feature Pack。不是單檔自解壓，也不使用 AppData／registry 存放偏好。Lite／Full 共用檔案級增量更新引擎，只用各自的檔案清單；Full 的 runtime 沒變就不下載，Lite 不會安裝系統 runtime。

版號以 `src/Radio.Windows/Radio.Windows.csproj` 為準；Full runtime 版本／來源以 `scripts/runtime-package.json` 為準。**.NET 8 支援於 2026-11-10 結束**，長期發行前需另行升級受支援的 runtime，不把舊版實驗機的 runtime 當成發行版本。

目前版號採 `major.feature.fix`：新增功能升第二碼，問題修正升第三碼；本次含增量更新功能的版本為 **v0.5.0**。

## 開發與發行分開

- 平常用根目錄 `Run.cmd` 建置並執行 Debug，不會產生發行包。
- 只有收到發行授權才執行根目錄 `Release.cmd`；它依序產生 Lite／Full 資料夾與 ZIP，成功後開啟 `dist/`。不為一般開發自動封裝。
- 中間產物在 `artifacts/release-build/`，不更新開發用 `bin`。新包使用含版號及時間的資料夾，不覆寫／刪除舊包或舊設定。
- 舊 `Build-Portable.cmd`、`scripts/Publish-Portable.ps1` 與 `src/Radio.Portable` 已移除，不再使用。

需要 .NET 8 SDK。可在已信任的 PowerShell 工作階段分別執行：

```powershell
# Lite
powershell -NoProfile -File scripts/Publish-Release.ps1
# Full
powershell -NoProfile -File scripts/Publish-Release.ps1 -SelfContained
```

不擅自修改全機 PowerShell 執行政策。失敗必須檢查輸出，不能把部分包當成成功發行。輸出格式為 `dist/Radio-Portable-v版本-{Lite|Full}-win-x64-時間/` 及同名 ZIP；產物不進 Git。

### 增量更新資產（僅授權封裝時產生）

`Publish-Release.ps1` 在 ZIP 前呼叫 `New-UpdateAssets.ps1`，並產生：

- Portable 資料夾內的 `radio.update-manifest.json`：此包管理的檔案與 SHA-256 基準，包含在手動安裝 ZIP，不含設定、不自我引用。
- 共用 `dist/Radio-Update-v版本-win-x64/`：`Radio-Update-v版本-Lite-win-x64.json`、`Radio-Update-v版本-Full-win-x64.json` 及 `Radio-File-<壓縮檔 SHA-256>.gz`。每個物件只有一個檔案，兩模式相同內容去重共用；清單記錄原檔及壓縮檔的大小、SHA-256、相對路徑與資產名。
- 同版本／類型的既有清單不覆寫；重新打包前先移走舊的該版本更新輸出，避免混用不同建置。不能只替換清單而沿用不符的 ZIP。

正式發行時，除了手動安裝 ZIP，**必須把共用更新資料夾內所有 JSON 和 GZ 檔以原檔名上傳到同一個 `v版本` GitHub Release 根層級資產**，不是再把該資料夾壓成一個 ZIP。確認 GitHub 清單資產已有 `sha256:` digest、兩模式清單版本正確，且引用的所有物件皆存在，才把 Release 設為 latest。使用者端沒有整包下載備援；缺清單則不顯示箭頭，缺物件則安全中止並保留舊版。

`New-UpdateAssets.ps1` 可對小型離線 fixture 驗證清單／壓縮協定，不會執行建置或發行；這不等於已驗收完整 Lite／Full 封裝。

## 必須保留的 docs 檔案

`scripts/Publish-Release.ps1` 會依模式複製：

- `docs/portable-readme.txt` → Lite 的 `READ-ME.txt`。
- `docs/portable-full-readme.txt` → Full 的 `READ-ME.txt`。
- `docs/third-party-notices.txt` → 兩包的 `THIRD-PARTY-NOTICES.txt`。

Full 另從實際 runtime 套件複製原始 LICENSE／NOTICE 到 `THIRD-PARTY/`。不能只刪授權通知來縮小檔案，也不能因 `docs` 不是執行相依就整目錄刪除。

## 設定與驗收

- `radio.settings.json` 位於程式同目錄；更新本機包時保留使用者原設定，ZIP 不附個人偏好、憑證或短效串流 URL。唯讀資料夾只支援當次調整。
- 封裝驗收要執行解壓後的 `RadioMini.exe`，不是 `dotnet run`；Full 須確認實際使用包內 `coreclr.dll`。
- 至少檢查啟動／退出、設定保存、系統匣及工作列最小化／還原；有聲播放、線上各台、長時間、睡眠／斷網恢復與實體多螢幕應依發行範圍另行驗收。
- 開發用 console 回歸見根目錄 [README](../README.md)。離線假資料／靜音 WAV 與模擬 DPI 不替代線上串流、人工聽音或實體多螢幕驗收。
- 不以已過期的 ZIP 大小、CPU／RAM 短測宣稱現行效能；不強制清空 working set 或 GC 美化數值。量測範圍及限制需隨當次結果交代。

## 一鍵更新

- 僅已封裝的 `RadioMini.exe`（不含開發用 PDB）自動檢查：啟動一次，之後每小時一次；開發模式、probe、離線 UI 測試不連線檢查更新。查詢本專案 GitHub `/releases/latest`，只接受較新的 `v主.次.修訂` 正式版，不接受 draft／prerelease 或降版。
- 有可用更新時，右上角出現下載箭頭，懸浮顯示目標版本。無更新、無網路、API 限流或沒有符合安全條件的資產時不彈窗、不影響播放，下一小時再檢查。
- 點擊後箭頭停用，先下載並以 GitHub 資產 `digest` 驗證同類型 x64 JSON 清單，再在背景計算本機檔案 SHA-256。**只下載新增、內容不同或缺少的檔案物件**，不下載完整 ZIP，也不對單檔做二進位差分。懸浮顯示所需物件的下載百分比；比對及下載期間仍可播放。優先依本機清單辨識 Full／Lite，沒有舊清單才以 `coreclr.dll` 是否存在判斷，不切換類型。Full 只有 runtime 內容變動／損壞／缺少時才下載相關 runtime 檔；Lite 不安裝／升級系統 .NET。若 runtime 損壞到程式已無法啟動，仍需手動修復。
- JSON、原檔及 GZip 物件都有大小／SHA-256 驗證；拒絕版本／類型不符、設定檔、路徑穿越、連結、保留檔名、重複路徑、檔案／目錄衝突與不完整清單。來源限本專案 GitHub Release；不是額外的程式碼簽章機制，安全性仍依賴 GitHub 帳號與 HTTPS。
- 暫存位於 `%TEMP%/RadioMini-update-<隨機值>/`。下載變更並驗證完成、外部更新程序回報就緒後，播放器保存設定並退出。程序在退出後再次核對所有未變動本機檔案、待寫入新檔與待刪舊檔，才備份／替換／刪除，完成後重啟 `RadioMini.exe`。未變動檔案不覆寫；每次一起更新同目錄的 `radio.update-manifest.json`。
- 刪除只限**上版清單有追蹤、這版已移除且仍符合舊 SHA-256** 的檔案；已被手動修改的待刪檔會阻止更新，請先備份／移走後重試。設定檔與清單外自建檔案不刪除。有舊清單時，新增檔案若撞名未追蹤且內容不同的本機檔，也會阻止更新而非覆蓋。沒有舊清單的首次過渡更新不猜測舊檔歸屬，可能保留歷史殘留；不自動刪除空目錄。
- 更新使用 Windows PowerShell 5.1，僅子程序帶 `-ExecutionPolicy Bypass`，不更改使用者或全機政策；組織政策仍可能禁止，這時請手動更新。程式資料夾須可寫，不提權；目的路徑有 junction／symlink 時拒絕安裝。請先關閉同資料夾的其他播放器實例。
- 下載／驗證／啟動程序失敗：保留原播放器，可點擊重試或手動下載，**不自動降級成全量下載**。替換／刪除失敗：逐檔嘗試還原修改、刪除與清單，移除這次新增檔，保留備份並顯示錯誤提示。查看暫存目錄的 `result.txt`，確認後重新開啟舊版；若還原也被鎖檔阻擋，先關閉相關程序再從 `backup/` 還原。成功則清除 payload 與備份，保留清單、計畫及小型記錄；失敗暫存可在確認復原後手動刪除。
- 不保證斷電／磁碟毀損下的跨檔案原子性，也無法判斷新版本啟動後是否正常播放。檔案與目錄互換的結構變更會停止並要求手動更新。新 Windows／runtime 相依條件改變時仍須依 Release 說明處理。
- **v0.4.0 不含更新器**，第一次需手動下載含此功能的版本並保留原設定。此功能不會自行建立 Release；正式端到端更新必須在授權發布後，以兩個實際 portable 版本驗收。`--updates` 使用同一份資產產生腳本及實際外部程序，驗證 Lite／Full 選擇性下載、新增／修改／刪除／缺檔修復、設定保留、鎖檔還原與安全檢查；測試不重啟真實播放器，不代表 GitHub 發行與完整自我更新已驗收。

### 發布後的真實更新驗收

明確授權並發布 latest 正式 Release 後，可用 `scripts/Test-PortableUpdate.ps1 -BaselineZip <含更新器的較舊測試ZIP> -ExpectedVersion 0.5.0` 驗證。腳本只會在新建的 `artifacts/update-e2e-隨機值/` 解壓並啟動，透過 UI Automation 點實際箭頭，等待舊程序退出與新程序重啟，核對所有檔案雜湊、設定、自建檔及 Full 實際載入包內 runtime，結果存入該資料夾的 `result.json`。請勿把歷史正式 v0.4.0 當成含更新器的基準。測試使用空收藏避免連線電台，不代表已驗收有聲播放、長時間或實體多螢幕。失敗保留隔離資料供診斷，不更動原 ZIP 或使用者 portable 資料夾。
