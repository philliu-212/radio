# Windows portable 封裝指引

此文件只記錄現行封裝方式，不再混放歷代 UI 草稿、效能表或「尚未實作」的舊計畫。歷史驗證結果請查 Git／Issue／PR；本文件不是新版本已完成線上或人工驗收的宣告。

## Lite 與 Full

| 包 | 相依條件 | 啟動 |
| --- | --- | --- |
| Lite | Windows 10 2004 以上、.NET 8 Desktop Runtime x64 | 解壓完整資料夾，雙擊 `RadioMini.exe` |
| Full | 同上 Windows，包內附 .NET runtime，不需另裝 .NET 或 SDK | 同上，必須保留整個資料夾 |

兩包使用同一個 `src/Radio.Windows` 專案，功能相同，不包含 mpv／FFmpeg。仍依賴 Windows 媒體元件；Windows N 版本需 Media Feature Pack。不是單檔自解壓，也不使用 AppData／registry 存放偏好。

版號以 `src/Radio.Windows/Radio.Windows.csproj` 為準；Full runtime 版本／來源以 `scripts/runtime-package.json` 為準。**.NET 8 支援於 2026-11-10 結束**，長期發行前需另行升級受支援的 runtime，不把舊版實驗機的 runtime 當成發行版本。

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
- 點擊後箭頭停用，懸浮顯示下載百分比；下載期間繼續播放。依目前資料夾是否含 `coreclr.dll` 選擇 Full 或 Lite，不切換類型。只接受現行命名的單一 x64 ZIP，來源限本專案 GitHub Release，使用 GitHub 資產 `digest` 驗證 SHA-256，拒絕設定檔、路徑穿越、符號連結、重複路徑及不完整套件。這不是額外的程式碼簽章機制，安全性仍依賴 GitHub 帳號與 HTTPS。
- ZIP 暫存在 `%TEMP%/RadioMini-update-<隨機值>/`。驗證完成且外部更新程序回報就緒後，播放器保存設定並退出；程序等待實際播放器退出才備份／覆寫包內檔案，之後重啟 `RadioMini.exe`。同目錄 `radio.settings.json`、自建檔案及原本不在新包中的檔案不刪除，因此更新資料夾可能保留舊 runtime 檔案。
- 更新使用 Windows PowerShell 5.1，僅子程序帶 `-ExecutionPolicy Bypass`，不更改使用者或全機政策；組織政策仍可能禁止，這時請手動更新。程式資料夾須可寫，不提權；目的路徑有 junction／symlink 時拒絕安裝。請先關閉同資料夾的其他播放器實例。
- 下載／驗證／啟動程序失敗：保留原播放器，可點擊重試或手動下載。覆寫失敗：逐檔嘗試還原，保留備份並顯示錯誤提示，請查看暫存目錄的 `result.txt`，確認後重新開啟舊版；若還原也被鎖檔阻擋，先關閉相關程序再從 `backup/` 還原。成功則清除 ZIP、解壓及備份，保留小型記錄；失敗暫存可在確認復原後手動刪除。
- 不保證斷電／磁碟毀損下的跨檔案原子性，也無法判斷新版本啟動後是否正常播放。新 Windows／runtime 相依條件改變時仍須依 Release 說明處理。
- **v0.4.0 不含更新器**，第一次需手動下載含此功能的版本並保留原設定。此功能不會自行建立 Release；正式端到端更新必須在授權發布後，以兩個實際 portable 版本驗收。`--updates` 只驗證離線版本／ZIP 安全與外部程序替換、保留設定、鎖檔回復，不代表真實 Release 自我更新已驗收。
