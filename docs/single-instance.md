# Windows 單一實例與視窗喚醒

追蹤：[Issue #70](https://github.com/philliu-212/radio/issues/70)、[Issue #98](https://github.com/philliu-212/radio/issues/98)（目錄隔離）。

## 行為與範圍

- 正常播放器啟動在 OTA／音訊／MainForm 初始化之前取得 Named Mutex。同一使用者 SID、同一 Windows Session、同一啟動 EXE 目錄共用實例識別；不同目錄可各自執行。
- EXE 目錄取自 `Environment.ProcessPath`，不取工作目錄或 managed assembly 目錄。路徑轉為絕對路徑、移除尾端分隔符並統一大小寫後計算 SHA-256；Mutex／Pipe 僅含雜湊，不含原始路徑。Portable 使用根目錄 apphost，因此內部 package 版本切換不改變識別。此正規化不解析 junction／符號連結或短檔名別名。
- 第二個啟動程序只通知原程序，不載入設定、不建立播放器／Tray 圖示。通知成功正常退出；通知逾時退出碼為 1，不另開播放器。
- Mutex 在 `Local` 命名空間；Named Pipe 名稱含 SID 與 Session ID，接收端使用 `CurrentUserOnly`。
- Mutex 最多等待 250ms，涵蓋程序終止後執行緒清理的短暫競態；abandoned mutex 視為成功取得所有權。
- `ActivateWindow` 是單位元組命令（值 1）；接收端排入 UI thread 後回覆相同位元組。ACK 代表已排程，並非 Windows 保證授予前景焦點。
- IPC 最多重試 20 次、總期限 8 秒；單次連線 250ms、交換 1 秒，重試間隔 100ms。無效命令／斷線不停止接收端。
- 啟動端查詢已連線 pipe 的真正 server PID，再呼叫 `AllowSetForegroundWindow`。原程序共用既有 `ShowPlayer()`，恢復顯示、工作列與最小化狀態，嘗試取得前景。不使用 TopMost 或強制結束其他播放器。
- 真正結束時取消並關閉 pipe，於啟動 STA thread 釋放 mutex；異常終止則由 Windows 回收所有權。沒有持久化的單一實例鎖檔。
- 原有 portable OTA 檔案 lease 保留，不是這次新增的 mutex；診斷用 `--probe` 不參與互動播放器的單一實例判定。

舊版本若未實作此協定，或仍使用跨目錄共用的舊識別，不會與新識別互相喚醒；首次使用新版前須自行正常退出舊版，避免同目錄同時有新舊播放器。程式不會強制結束它。

## 自動驗證

依序執行（避免共用建置產物發生寫入衝突）：

```powershell
dotnet build src/Radio.Windows -c Release
dotnet run --project tests/Radio.Core.Tests -c Release
dotnet run --project tests/Radio.Windows.Tests -c Release -- --single-instance
dotnet run --project tests/Radio.Windows.Tests -c Release -- --ui
dotnet run --project tests/Radio.Windows.Tests -c Release -- --system
git diff --check
```

跨程序 fixture 使用獨立 GUID 命名，不連線或終止使用者的播放器。涵蓋目錄大小寫／尾端分隔符／等價路徑、不同目錄同時取得所有權及各自喚醒、延遲 pipe、快速連續啟動通知、無效指令、正常退出、保留 kernel handle 的 abandoned mutex 與有限逾時。目錄 fixture 使用合成路徑，不是實體 portable 封裝驗收。MainForm fixture 驗證顯示／工作列最小化／Tray 隱藏後還原同一 HWND，並保留播放、電台、音量、搜尋及既有 Tray 圖示。

## 人工驗收（尚未執行）

請以含本功能的實際 EXE 操作，工作列釘選必須指向同一套新版 EXE：

- [ ] 正常開啟後切到其他應用，再執行 EXE：原視窗取得前景。
- [ ] 原生工作列最小化後再執行 EXE：同一視窗還原。
- [ ] 用既有 Tray 最小化按鈕隱藏，再點釘選圖示：同一視窗顯示且取得前景。
- [ ] 連續快速點擊釘選圖示：沒有第二個播放器／Tray 圖示。
- [ ] 真實串流播放中重複上述操作：不斷音、不重連，電台、音量、搜尋維持原狀。
- [ ] 真正結束後重新啟動：正常建立新實例。
- [ ] 兩個 portable 目錄可同時開啟，各自重開 EXE 僅喚醒所屬視窗；設定各自保留。
- [ ] 同一 portable 根目錄完成 OTA 版本切換後，重開根目錄 EXE 仍只喚醒該目錄的視窗。
- [ ] 僅對測試播放器模擬異常終止，再啟動：正常建立新實例。

自動 fixture 不等同於真實 Shell 釘選操作、Windows 前景權限或線上串流聽感驗收。不同使用者／不同 Session 的隔離目前由命名設計保障，尚未進行多使用者實測。
