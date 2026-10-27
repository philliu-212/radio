# Radio

個人用、低資源優先的 Windows 網路廣播**驗證原型**。原生 WinForms 介面，直接播放官方音訊來源，不啟動瀏覽器、WebView 或音訊代理伺服器。

提供 Windows 原生與可選的 mpv 純音訊播放引擎；mpv 的本機安裝、測試與授權注意事項見 [docs/mpv.md](docs/mpv.md)。

## 電台

| 電台 | 顯示頻率 | 串流 |
| --- | --- | --- |
| 好事989 | 98.9 | 動態 HLS |
| KISS Radio | 99.9 | 動態 HLS |
| POP Radio 台北 | 91.7 | 動態 HLS |
| 飛碟電台 | 92.1 | Revma |
| 中廣流行網 i like radio | 96.3 | Revma |
| Hit FM 北部 | 107.7 | 動態 HLS |

頻率是使用者識別用標籤，各地頻率不同；網路節目可能與地方 FM 訊號不同。96.3 對應中廣流行網，不是 Alian。

## 執行

需要 Windows 10 2004 以上（含媒體播放元件）、.NET 8 SDK 與網路。開發時沿用現有 SDK；正式發行前應更新至仍受支援且已修補的 SDK／runtime，並重新量測。

```powershell
dotnet run --project src/Radio.Windows -c Release
```

已備妥本機 mpv 時可用 `dotnet run --project src/Radio.Windows -c Release -- --backend mpv` 切換。視窗標題會顯示目前引擎。

- 選台後按「播放」或雙擊，不會自動播放。
- 「停止」會釋放播放來源；再次播放重新取得來源，回到直播，不保留暫停期間內容。
- 可調整音量、靜音，縮小至系統匣繼續播放；關閉視窗會退出。
- 來源解析或播放失敗時可手動重試，或開啟官方頁面。本原型不做應用程式層自動重連，不繞過地區／播放時限／存取限制。
- 尚未加入我的最愛、設定持久化、開機自啟、錄音或時移。

## 測試與量測

離線測試使用固定假資料，不會連線電台；此專案是無第三方測試框架的簡單 console 測試程式，請用 `dotnet run`，不是 `dotnet test`。

```powershell
dotnet run --project tests/Radio.Core.Tests -c Release
dotnet build src/Radio.Windows -c Release
powershell -NoProfile -File scripts/Smoke-Test.ps1
```

UI 冒煙測試只操作自己啟動的測試視窗；加 `-Playback` 可測試第一台的靜音播放，預設不連線收聽。
若系統政策不允許執行 `.ps1`，可改用手動操作驗證；不需要更改全機 PowerShell 執行政策。

靜音線上實驗（會產生少量正常收聽流量）需以已編譯的程式執行，避免把 SDK／建置程序算入播放器：

```powershell
$exe = '.\src\Radio.Windows\bin\Release\net8.0-windows10.0.19041.0\Radio.Windows.exe'
Start-Process $exe -ArgumentList '--probe --seconds 15 --output artifacts/probe.json' -Wait
```

可加 `--station hit-north` 只測一台；ID 見 `src/Radio.Core/stations.json`。每台先等播放狀態、暖機 3 秒，再檢查播放時鐘是否持續前進及取樣；結束後釋放來源。報告不儲存帶 token 的串流 URL。

新量測包含播放器程序與它擁有的播放子程序，不含共享 Windows 音訊服務或 GPU 記憶體；短測不代表長期穩定，也不代表已確認實際音訊可聽。沒有相同來源的瀏覽器基準前，不宣稱相對瀏覽器的節省比例。

原生引擎實測與 KISS 影像軌問題見 [docs/experiments.md](docs/experiments.md)；[mpv 對照實驗](docs/mpv.md)中，KISS 的合計 private 記憶體約從 137 降至 51 MiB。原生引擎的影像開銷仍未消除，mpv 提供不啟用影像軌的另一條播放路徑。

## 分層與手機版

```text
src/Radio.Core/       # 電台 JSON、官方來源解析、IStreamResolver / IAudioPlayer 契約
src/Radio.Windows/    # WinForms、Windows 原生 MediaPlayer／HLS 引擎、線上實驗
tests/Radio.Core.Tests/
tests/Radio.Windows.Tests/ # 本機 WAV／mpv 程序生命週期測試
docs/                # 來源調查與實驗紀錄
artifacts/           # 本機實驗產物（不進 Git）
```

核心不參考 Windows API。日後可由 .NET Android／iOS 專案沿用核心，另寫原生音訊與背景播放整合；若改用 Kotlin／Swift，仍可沿用電台 JSON、規格及來源測試案例。共用介面不代表各平台播放行為完全相同。

電台與音訊內容權利屬各權利人。只取得官方公開收聽入口，不複製電台網站、Logo 或節目內容，也不對外轉播。來源可能更換或限制特定地區／裝置。
