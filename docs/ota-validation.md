# OTA 驗收紀錄（v0.5.0 重作候選）

## 2026-10-30：根主程式＋獨立 updater（本次驗收）

本機 Windows x64；唯一入口為根 `RadioMini.exe`，沒有 Launcher 或內層同名播放器 EXE。根 EXE 為 222,208 bytes，內部 updater 為 94,720 bytes。兩個模式的根 ProductName／FileDescription 都是 `Radio Mini`，FileVersion 與播放器版本一致；平常啟動沒有 updater 程序，Full 實際從 hash 內載入 coreclr.dll。

使用目前程式碼 publish 的 1.0／1.1／1.2 測試包及合成 identity，**不發行**。完整 ZIP 經 loopback TCP/HTTP，空收藏、`--no-autoplay --no-update-check`，不連線廣播；不是 GitHub WAN／人工聽音測試。

| Latest Wins 1.0→待 1.1→待 1.2→直升 1.2 | Lite | Full |
| --- | ---: | ---: |
| 1.1 ZIP | 6,351,502 bytes | 76,319,262 bytes |
| 1.1 背景下載＋SHA-256 驗證 | 0.402 秒 | 0.911 秒 |
| 1.1 解壓＋完整檔案驗證＋準備 | 0.859 秒 | 9.870 秒 |
| 1.2 ZIP | 6,351,510 bytes | 76,319,268 bytes |
| 1.2 背景下載＋SHA-256 驗證 | 0.400 秒 | 0.906 秒 |
| 1.2 解壓＋準備＋淘汰舊 Pending | 0.889 秒 | 10.563 秒 |
| 重啟請求→新版主視窗 | **3.081 秒** | **2.954 秒** |
| 初始磁碟用量 | 22,754,880 bytes | 190,793,201 bytes |
| 最新 Pending 備妥 | 45,507,935 bytes | 381,527,653 bytes |
| 健康成功後（已刪舊資源與回復備份） | 22,849,546 bytes | 190,859,405 bytes |

另跑正常 1.0→1.1 兩模式皆通過，重啟為 Lite 3.766 秒／Full 3.630 秒；這是單機單輪量測，不是效能上限或統計分位數。

四輪都驗證：背景不中止原程序／不替換根入口、重查零重複下載、單一資料 owner、名稱／版本同步、沒有內層播放器 EXE、成功刪舊版與設定保存，以及以下四種真實 updater 回復：

- 無效 PE：換入的根 EXE 無法啟動，還原入口及設定。
- 錯誤資源綁定：有效 apphost 載入另一個 package，managed 初始化拒絕，不誤判健康成功。
- 缺少 managed DLL：換入的真實 apphost 指向已移除的 hash；關閉 .NET GUI error，偵測退出並回復。
- 鎖住 bin/updater.exe：允許備份讀取但拒絕第二入口替換；第一入口已替換後仍能完整回復。

原始報告不進 Git：`artifacts/ota-integration-<id>/result.json`。Latest Wins：Lite `71f0ca4f901047ff8932354d13ea05f0`、Full `a656e5ff7197406c923e0b4774b91dce`；正常：Lite `42960a6c2c7243018bde7585c3e0d7cf`、Full `65c413aef2ac4c94a933265c97340b89`。

時間量測從腳本寫入相同 SwitchRequest／啟動 bin/updater.exe、關閉舊視窗，到新根程序 MainWindowHandle 非零；不是人工點擊或首幀追蹤。背景時間為狀態轉換 Stopwatch，磁碟為檔案長度總和，不含 NTFS allocation／尖峰 ZIP 空間。無效新包注入用於切換測試，不代表背景 verifier 會接受未通過雜湊的包。

本次建置、Core console、Windows `--ui`／`--system`／`--updates` 均通過。新增離線測試覆蓋兩入口 SHA-256／缺少 updater、背景不替換入口、第二入口替換失敗、成功清除 Previous／備份、損毀主 state 不會從 `.bak` 復活已刪版本、延後回收舊 worker 副本。PowerShell 語法檢查通過。CI 已加入同樣兩模式整合流程，但尚未在 GitHub hosted runner 執行，不聲稱 CI 已通過。

失敗輪次也保留在 artifacts：早期 `c042...`／`6438...` 的新寫入 PE 暫存目錄搬移被 Windows 拒絕；`9de9...` 的入口替換遇到檔案占用並回到舊版，不能算成功升級。加入有限次 I/O 重試、測試釋放已退出程序 handle 後重建並重跑，以上四輪才是最終通過結果；未停用安全軟體或停止使用者的既有包。

限制仍見文末與 [ota.md](ota.md)：尤其無常駐 Launcher 時，斷電後若 apphost 在 managed 初始化前就載入失敗，需手動恢復已知良好 Portable；首次試啟有 updater 監督時可自動回復。這次未移動 v0.5.0 tag、未合併 PR、未公開 Release。

---

## 以下為 2026-10-29 舊 Launcher 候選的歷史紀錄（已被取代）

**以下數字不是目前無 Launcher 架構的驗收，僅保留歷史重跑紀錄。**

2026-10-29，本機 Windows x64。不是新的正式 Release，原 v0.5.0 已撤回 draft，未移動 tag。

## 真實 Portable／程序測試

使用當時播放器原始碼另建 `1.0.0`／`1.1.0`／`1.2.0` 測試包（合成測試 identity，不發行），實際 .NET 8 WinForms＋穩定 .NET Framework Launcher。完整 ZIP 經 loopback TCP/HTTP 傳輸，**不是 GitHub WAN 速度**；空收藏、`--no-autoplay --no-update-check`，不連線廣播。

| 項目 | Lite | Full |
| --- | ---: | ---: |
| 1.1 ZIP 大小 | 6,344,671 bytes | 76,314,233 bytes |
| 1.1 背景下載＋ZIP 驗證 | 0.401 秒 | 1.274 秒 |
| 1.1 背景解壓＋逐檔驗證＋準備 | 0.663 秒 | 11.989 秒 |
| 1.2 ZIP 大小 | 6,344,671 bytes | 76,314,245 bytes |
| 1.2 背景下載＋ZIP 驗證 | 0.361 秒 | 0.888 秒 |
| 1.2 背景解壓＋準備＋淘汰舊 pending | 0.633 秒 | 10.930 秒 |
| 要求重啟→1.2 主視窗可見 | 2.173 秒 | 2.190 秒 |
| 初始磁碟用量（含測試設定／狀態） | 22,735,004 bytes | 190,844,228 bytes |
| 1.1 備妥後 | 45,375,568 bytes | 381,350,495 bytes |
| 1.2 取代 1.1 pending 後 | 45,376,530 bytes | 381,413,656 bytes |
| 切換完成（保留回復版本） | 45,377,114 bytes | 381,414,239 bytes |

Launcher 本身為 89,600 bytes，含既有多尺寸圖示；不打包另一份 .NET runtime。

測試結果兩模式均通過：

- 保持 1.0 程序／視窗不退出，先準備 1.1 再準備 1.2，直接切換至 1.2，沒有啟動 1.1。
- 再查同一待更新版本沒有額外下載；舊 pending 資源目錄清理，磁碟未累積三套版本。
- 重複啟動根入口不建立第二個資料 owner；真正切換等待舊程序退出。
- 完整 ZIP、全部 payload 檔案雜湊、狀態、偏好／統計、自建檔保留；Full 實際載入 hash 目錄的 coreclr.dll。
- 另注入無效 PE 新版，真實 Launcher 啟動失敗後自動回到上一個可用程序與資料快照，再正常退出。

量測方式：背景兩個時間由 `Stopwatch` 的 Downloading／Preparing／Ready 狀態量測；網路 body bytes 由 loopback server 記錄。重啟時間從腳本發出與 UI 相同的 Launcher 重啟請求並關閉舊視窗，到新程序 MainWindowHandle 非零；不含使用者操作時間，不是渲染第一幀的精密追蹤，也不是實體人工按鍵計時。磁碟為檔案長度總和，不是 NTFS allocation／尖峰空間；準備過程短暫含 ZIP＋解壓資料，尖峰可能較高。

本機原始報告（不進 Git）：`artifacts/ota-integration-*/result.json`，以實作提交 `45f71e8` 重建後，最終跨版測試 Lite `05b66463eff243a7b16b4b3772de51e8`、Full `75bc67b5775a4e118755dbe76c569ab9`。另跑正常 1.0→1.1，Lite `d8b1e7effa9445b08848dba3c1aaf500`、Full `60d289c6488e477e80920f4644422c59` 皆通過（重啟分別 2.169／2.097 秒）。

正式打包腳本亦在 `45f71e8` 上本機執行（未發行）：Lite ZIP `20261029-222836` 為 6,350,505 bytes，Full ZIP `20261029-222853` 為 76,345,073 bytes；兩包解壓後從根 Launcher 啟動、舊根設定自動複製進 data、原檔保留、設定保存、正常退出與 Full 包內 runtime 均通過。`SHA256SUMS-v0.5.0.txt` 兩筆和實際 ZIP 雜湊一致。與上表測試包不同，正式候選包含使用說明與原始授權通知。

中途曾有失敗輪次：腳本以不允許 delete sharing 的讀取和 state 原子替換衝突；無效 PE 在 Windows loader 可能阻塞 Process.Start。已改成允許原子替換的讀取、等待 Launcher 完成確認再進下一步，並於 Launcher 預檢 PE header／x64 machine、抑制 OS 載入錯誤視窗，加入有界診斷紀錄。上表及四輪結果都是修正後重跑，沒有把失敗輪次寫成通過。

## 離線／UI 回歸

`--updates` 已驗證兩模式的數值版本比較、排除 draft／prerelease、準備不切換 Current、Latest Wins、取消舊下載／關閉時取消、去重、指數退避、中斷後重試、新版失敗保留舊 Pending、ZIP hash／路徑穿越／重複檔／symlink／schema／清單不完整防護、排他檔案鎖、原子 state 備份與未確認 Trial 的設定回復。另實際執行遷移腳本，驗證 schema 1 收藏／音量／主題／次數原封複製且不覆寫目的設定，及未支援 schema 99 明確拒絕。

小型假包的數值保存在 `artifacts/ota-offline-metrics.json`，**不當成上表的真實 Portable 數值**。既有 UI／核心／系統音訊回歸與最終實作提交的驗證結果另外列在 PR。

## 尚未實測／不宣稱通過

- 新協定正式 GitHub Release 的 WAN 下載／公開發行：尚未執行，等待 PR 與 tag／草稿資產處理確認。
- 真實電台在背景完整 Full 下載時的人工聽音／長時間穩定：尚未執行；UI 假播放器只驗證狀態變動不停止或重開音訊。
- 實體斷電／磁碟損毀／磁碟滿、網路分享磁碟、各種安全軟體／唯讀路徑政策與實體多螢幕。
- 健康握手之後才發生的崩潰不會自動回復；此健康訊號不是音訊或完整應用功能的長期健康檢查。
