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
