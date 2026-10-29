# Radio

個人用、低資源優先的 Windows 網路廣播**驗證原型**。原生 WinForms 介面，直接播放官方音訊來源，不啟動瀏覽器、WebView 或音訊代理伺服器。

使用 Windows 系統音訊：六台走純音訊模式，Hit FM 走相容路徑，不需要外部播放器。mpv 後端、安裝工具及二進位已移除；[舊實驗紀錄](docs/mpv.md)僅供歷史參考。

**可雙擊的 Lite／Full portable 包**見 [docs/portable.md](docs/portable.md)；兩者都不附 mpv，Full 內含 runtime、不必另裝 .NET。現採使用者指定的 [Direction C 橫向 UI](docs/ui-direction-c.md)，支援深／淺色與展開時的官方節目／歌曲資訊。

## 電台

| 電台 | 顯示頻率 | 串流 |
| --- | --- | --- |
| POP Radio 台北 | 91.7 | 動態 HLS |
| 飛碟電台 | 92.1 | Revma |
| 中廣流行網 i like radio | 96.3 | Revma |
| 好事989 | 98.9 | 動態 HLS |
| KISS Radio | 99.9 | 動態 HLS |
| Hit FM 北部 | 107.7 | 動態 HLS |
| RFI Monde | WEB | MP3 64 kbps |

頻率是使用者識別用標籤，各地頻率不同；網路節目可能與地方 FM 訊號不同。96.3 對應中廣流行網，不是 Alian。

## 開發執行

雙擊根目錄 **`Run.cmd`**：建置並執行目前原始碼，不會產生發行包。只保留 `src/Radio.Windows` 一個程式專案，視窗左上角直接顯示版本（系統匣右鍵選單也可查看）。修改後需先關閉舊開發視窗再重開。

要交付時才雙擊 **`Release.cmd`**：從同一專案產生 Lite／Full 資料夾與 ZIP 到 `dist/`，不啟動程式、不更新開發用 `bin`。`src/Radio.Portable` 已移除，`Build-Portable.cmd` 已改名。

需要 Windows 10 2004 以上（含媒體播放元件）、.NET 8 SDK 與網路。開發時沿用現有 SDK；正式發行前應更新至仍受支援且已修補的 SDK／runtime，並重新量測。

```powershell
dotnet run --project src/Radio.Windows
```

- 啟動時不自動選取或播放；首次沒有「我的電台」時直接開啟雙清單管理視窗。從內建電台庫選取後按 → 加入、← 移除，右下角 ✓ 完成。允許 0 台完成，返回中央 + 畫面，點 + 可重新進入管理。
- 管理搜尋列左右各有獨立地球語言篩選（全部／中文／English／Français），可與名稱、頻率搜尋同時使用；依電台播放語言資料篩選，不猜測名稱或國家。成功播放次數變動時即時排序，不切斷播放；主標題懸浮可查看版號。
- 主畫面搜尋框可即時搜尋名稱／頻率，不影響正在播放的直播；右側 + 開啟管理視窗。左右管理清單各有獨立搜尋及捲動，直接點擊可選取／取消多台。中間上方 ›／‹ 處理選取電台，下方 »／« 處理該側全部搜尋結果（無搜尋時即全部電台），不需手填串流 URL。
- 點選電台直接開播／切台；我的電台依累積成功播放次數排列，同次數維持原順序，只在啟動／完成管理時重排。清單每頁五台，可使用細捲動條、滾輪或鍵盤捲動。
- 「停止」會釋放播放來源；再次播放重新取得來源，回到直播，不保留暫停期間內容。
- 可調整音量、靜音；右上角最小化按鈕縮到系統匣，點工作列則正常最小化／還原，皆繼續播放；關閉視窗會退出。
- 來源解析或播放失敗時可手動重試，或開啟官方頁面。本原型不做應用程式層自動重連，不繞過地區／播放時限／存取限制。
- 音量、深／淺色偏好、我的電台及播放次數保存在程式同資料夾的 `radio.settings.json`；舊版設定保留音量與主題，沒有選台資料則顯示中央 +。尚未加入開機自啟、錄音或時移。

## 測試與量測

離線測試使用固定假資料，不會連線電台；此專案是無第三方測試框架的簡單 console 測試程式，請用 `dotnet run`，不是 `dotnet test`。

```powershell
dotnet run --project tests/Radio.Core.Tests -c Release
dotnet run --project tests/Radio.Windows.Tests -c Release -- --system
dotnet run --project tests/Radio.Windows.Tests -c Release -- --ui
dotnet build src/Radio.Windows
powershell -NoProfile -File scripts/Smoke-Test.ps1
```

UI 冒煙測試只操作自己啟動的測試視窗；加 `-Playback` 可測試第一台的靜音播放，預設不連線收聽。
若系統政策不允許執行 `.ps1`，可改用手動操作驗證；不需要更改全機 PowerShell 執行政策。

靜音線上實驗（會產生少量正常收聽流量）需以已編譯的程式執行，避免把 SDK／建置程序算入播放器：

```powershell
$exe = '.\src\Radio.Windows\bin\Debug\net8.0-windows10.0.19041.0\Radio.exe'
Start-Process $exe -ArgumentList '--probe --seconds 15 --output artifacts/probe.json' -Wait
```

可加 `--station hit-north` 只測一台；ID 見 `src/Radio.Core/stations.json`。每台先等播放狀態、暖機 3 秒，再檢查播放時鐘是否持續前進及取樣；結束後釋放來源。報告不儲存帶 token 的串流 URL。

新量測包含播放器程序與它擁有的播放子程序，不含共享 Windows 音訊服務或 GPU 記憶體；短測不代表長期穩定，也不代表已確認實際音訊可聽。沒有相同來源的瀏覽器基準前，不宣稱相對瀏覽器的節省比例。

舊 Windows 相容路徑的實測與 KISS 影像軌問題見 [docs/experiments.md](docs/experiments.md)；當時的 [mpv 對照實驗](docs/mpv.md)中，KISS 的合計 private 記憶體約從 137 降至 51 MiB。相容路徑本身的影像開銷未消除，但不再是 KISS 的預設播放路徑。

後續已新增 Windows Media Engine 純音訊路徑，第一階段 portable 包 KISS 的 private 約為 **26 MiB（Lite）／26 MiB（Full）**，不需 mpv。最新 Direction C 包的顯示／系統匣／還原對照與封裝大小見 [portable 實測](docs/portable.md)。

## 分層與手機版

```text
src/Radio.Core/       # 電台 JSON、官方來源解析、IStreamResolver / IAudioPlayer 契約
src/Radio.Windows/    # WinForms、Windows 原生 MediaPlayer／HLS 引擎、線上實驗
tests/Radio.Core.Tests/
tests/Radio.Windows.Tests/ # 本機靜音 WAV／UI 回歸測試
docs/                # 來源調查與實驗紀錄
artifacts/           # 本機實驗產物（不進 Git）
```

核心不參考 Windows API。日後可由 .NET Android／iOS 專案沿用核心，另寫原生音訊與背景播放整合；若改用 Kotlin／Swift，仍可沿用電台 JSON、規格及來源測試案例。共用介面不代表各平台播放行為完全相同。

電台與音訊內容權利屬各權利人。只取得官方公開收聽入口，不複製電台網站、Logo 或節目內容，也不對外轉播。來源可能更換或限制特定地區／裝置。
