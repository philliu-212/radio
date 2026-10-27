# mpv 播放引擎實驗

## 安裝與執行

mpv 是可選的本機播放引擎。Windows 原生引擎仍可單獨執行，不需要 mpv。

`scripts/Install-Mpv.ps1` 只下載／解壓至被 Git 忽略的 `tools/mpv/`，不執行安裝器、註冊檔案關聯、更新 PATH 或更動系統設定。

```powershell
powershell -NoProfile -File scripts/Install-Mpv.ps1
dotnet run --project src/Radio.Windows -c Release -- --backend mpv
```

若 PowerShell 執行政策不允許 `.ps1`，可以依 `scripts/mpv-package.json` 手動下載、核對 SHA-256 並解壓至 `tools/mpv/`；不需要變更全機執行政策。亦可透過 `--mpv-path C:\trusted-path\mpv.exe` 指定自行安裝且信任的執行檔。

原生引擎：`--backend native`（目前預設）。不會因缺少 mpv 而暗中下載、改用其他播放器或開啟瀏覽器。

## 取得來源與版本

- mpv 官方安裝頁：[mpv.io/installation](https://mpv.io/installation/)。該頁明示一般 Windows 發行檔為**非官方第三方建置**。
- 採用該頁列出的 `shinchiro/mpv-winbuild-cmake`，release `20261021`。
- 檔名與 SHA-256 已固定在 `scripts/mpv-package.json`，以 GitHub release 的 asset digest 核對下載內容。此核對不是獨立數位簽章或安全稽核。
- 實測版本：`mpv v0.41.0-1227-ge471496c5`，FFmpeg `N-127893-g9a861e968`。
- 保留原封包在 `tools/`；沒有把第三方二進位檔加入 Git，也沒有重新發布安裝包。
- 此建置的 `mpv.exe` 約 **115.50 MiB**，下載封包約 **32.45 MiB**；執行記憶體較省不等於部署檔案較小。
- 將來若要分發播放器與 mpv／FFmpeg 等二進位，必須先確認該建置的 GPL／第三方授權、通知及對應原始碼提供義務；不可直接當成無附帶條件的單一 exe 發行。

## 資源與行為

- 每次播放建立一個無視窗 mpv 子程序；停止、切台、失敗或退出時終止**自己建立的那一個程序**。
- `--no-video` 關閉影像軌，`--audio-display=no` 不產生音訊封面顯示。不載入 mpv 使用者設定、自訂腳本、OSC 或 youtube-dl。
- 壓縮串流快取上限設為 4 MiB、讀前目標 5 秒、不保留回看快取；這不是整個程序記憶體的上限。
- 即使停用影像解碼，若 HLS 本身混合音訊與影像，仍可能需要下載含影像的分段。不能據此宣稱網路流量必然只剩音訊。
- 不開 TCP 控制埠。以每次隨機命名的本機 named pipe 與 JSON IPC 控制播放。
- IPC 是本機控制通道，非網路安全服務；不向其他程式公布或提供遠端控制功能。隨機管線名不等於完整的本機惡意程式防護。
- 帶簽章的串流 URL 透過 IPC 傳入，不放進程序命令列，不開 mpv log、不解析終端輸出。設定與報告不保存這些 URL。
- 網址依然由 `Radio.Core` 的官方來源解析器取得；不會重新引入 TWeFM 代理。

## 測試

```powershell
# 產生本機靜音 WAV；測試本身不連電台、不播放可聽聲音。
dotnet run --project tests/Radio.Windows.Tests -c Release

# 原生介面測試，可切換引擎。
powershell -NoProfile -File scripts/Smoke-Test.ps1 -Backend mpv -Playback

# 線上短測；報告包含主程式與它擁有的 mpv 子程序。
dotnet src/Radio.Windows/bin/Release/net8.0-windows10.0.19041.0/Radio.Windows.dll --probe --backend mpv --station kiss-999,ufo-921 --seconds 60 --output artifacts/mpv-60s.json
```

私有記憶體、CPU 使用量會加總主程式與已知播放子程序；working set 的加總可能重複計算共享頁面，所以比較時同時列出兩者，不只挑較小的數字。共享音訊服務與 GPU 記憶體仍不在統計內。所有短測與 A/B 比較皆不等於長期穩定保證，且先後測試無法完全排除網路時段差異。

## 60 秒 A/B 結果（2026-10-27）

相同電台來源，先跑原生、再跑 mpv，均為 Release、靜音、3 秒暖機、60 秒取樣。兩種引擎的 KISS、飛碟均通過時鐘前進至少 90% 的檢查。

| 電台／引擎 | 程序數 | 平均 private MiB | 平均 working set MiB | CPU 全機 % | 開播秒數 | 時鐘前進秒數 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| KISS／原生 | 1 | 137.07 | 174.72 | 0.056 | 2.20 | 60.28 |
| KISS／mpv | 2 | 51.10 | 103.74 | 0.028 | 3.90 | 60.46 |
| 飛碟／原生 | 1 | 39.88 | 115.86 | 0.015 | 2.89 | 60.11 |
| 飛碟／mpv | 2 | 45.89 | 100.91 | 0.022 | 5.69 | 60.51 |

### 結論與取捨

- **KISS 有明確的記憶體改善**：包含主程式和子程序後，private 約從 137 降至 51 MiB（本次約減少 63%），工作集約從 175 降至 104 MiB。mpv 回報影像選擇為 false、音訊為 AAC；原生回報 640×360 影像。
- **不能說 mpv 已解決飛碟的緩衝問題**：本輪原生與 mpv 都通過 60 秒測試，前次緩衝也可能與網路／來源有關。事件次數涵蓋開播期，不能直接當成穩態中斷次數比較。
- 飛碟的 mpv private 比原生略高、開播較慢；不應因 KISS 的改善就宣稱所有電台都受益。
- 兩者 CPU 都很低，短測的小數點差異不適合當成穩定排名。
- 暫時**保留原生為預設，mpv 為可選**；收聽 KISS 可選 mpv。尚未自動按電台切換引擎，也不會在失敗後自動換引擎或重新取得串流網址。
- 所有結束／停止檢查中，自己啟動的 mpv 子程序均已退出。離線本機 WAV 測試另覆蓋播放替換、重複停止、開啟途中停止、預先取消、來源錯誤、音量／靜音命令及 Dispose。

原始本機報告：`artifacts/compare-native-60s.json`、`artifacts/compare-mpv-60s.json`。這是單次、先後順序的實驗，不是隨機交叉、多輪或長時間效能基準。

## 六台回歸與控制測試

mpv 六台各 15 秒回歸全數通過，合計 private 約 45.51～53.11 MiB、working set 約 98.60～106.25 MiB，CPU 全機約 0.009～0.060%。每次 Stop 後均確認擁有的子程序已退出。原始報告為 `artifacts/probe-mpv-six.json`。

Windows 本機 WAV 生命週期測試全數通過，包括三輪播放／停止、mute 與 volume 的 IPC 讀回、`vid=false`、時鐘前進、切換來源時清掉舊程序、預先取消不啟動程序、開啟途中 Stop 不會復播、錯誤來源清理及 Dispose。測試只使用產生的靜音 WAV，不連電台。

原生與 mpv 的 UI 冒煙測試均通過；mpv 額外經由實際 UI 測過靜音開播、停止、縮到系統匣與退出。仍未人工確認聲音內容或做長時間、睡眠喚醒／斷網恢復測試。
