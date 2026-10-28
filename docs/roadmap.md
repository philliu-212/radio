# 分階段計畫

核心需求：個人用台灣電台播放器、低記憶體／CPU、可雙擊啟動、portable、好看的深色介面；未來保留 Android／iOS 的分層空間。

## 第一階段：不依賴 mpv 與可執行輕量包

- [x] 自寫 Windows Media Foundation 純音訊控制層，不附帶 mpv／FFmpeg。
- [x] KISS 確認 audio-only、沒有啟用影像軌。
- [x] 獨立 portable 專案，不攜帶 mpv 程式碼；為保留 Hit FM，包含 Windows API 投影與相容路徑。
- [x] 解壓縮完整資料夾後可雙擊 Radio.exe，不必安裝 App 或 SDK。
- [x] 完成 Lite／Full 六台短測與解壓包 UI 驗證；Full 確認使用包內 runtime。

**環境限制必須明示**：Lite 仍需 .NET 8 Desktop Runtime；Full 內含 runtime，不需另裝 .NET，但體積較大。不可用「portable」掩蓋 Lite 的相依需求。使用 Windows 系統媒體元件不等於我們從零編寫音訊解碼器。

## 第二階段：介面與 dark mode

- [x] 依使用者指定 Direction C 實作橫向小視窗、電台清單、播放卡片與音量布局。
- [x] 深色為預設，可切換淺色並保存偏好。
- [x] 使用原生控制項及必要自繪，不引入 WebView／大型 UI framework。
- [x] 保留鍵盤操作、可讀性與 UI 自動化測試；DPI 訊息模擬通過，真實多螢幕驗收仍待人工。
- [x] 可選官方 metadata，展開時更新、縮小取消，節目／DJ／預告歌曲不混淆。

## 第三階段：縮小後的資源與穩定性

- [x] 將狀態顯示與播放核心分離：隱藏時不更新不必要的 UI，還原時一次更新。
- [x] 分別量測實際 Lite／Full 的視窗顯示、縮到系統匣、還原後 CPU、private bytes、working set；見 portable 文件（短測）。
- [x] 不以強制清空工作集／大量 GC 來宣稱省記憶體；播放解碼／必要緩衝不能任意移除。
- [ ] 長時間播放、重複切台、睡眠喚醒／斷網恢復測試。

## 後續封裝選擇

- 現有 .NET 輕量包：最小，但目標電腦需 Desktop Runtime。
- .NET self-contained：不需另裝 runtime，但會增加數十至上百 MB；不作為「極小包」宣稱。
- Windows 內建 .NET Framework／原生 Win32 路線：可能降低部署依賴與大小，但需另做移植、相容性及安全性驗證，並非更換一個 publish 參數就能完成。

在確認需求與實測之前，不為單一 exe 外觀犧牲低資源目標或把大型依賴藏進封裝。
