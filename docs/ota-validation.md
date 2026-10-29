# OTA 驗收紀錄（v0.5.0 重作候選）

2026-10-29，本機 Windows x64。不是新的正式 Release，原 v0.5.0 已撤回 draft，未移動 tag。

## 真實 Portable／程序測試

使用目前播放器原始碼另建 `1.0.0`／`1.1.0`／`1.2.0` 測試包（合成測試 identity，不發行），實際 .NET 8 WinForms＋穩定 .NET Framework Launcher。完整 ZIP 經 loopback TCP/HTTP 傳輸，**不是 GitHub WAN 速度**；空收藏、`--no-autoplay --no-update-check`，不連線廣播。

| 項目 | Lite | Full |
| --- | ---: | ---: |
| 1.1 ZIP 大小 | 6,343,828 bytes | 76,313,404 bytes |
| 1.1 背景下載＋ZIP 驗證 | 0.369 秒 | 0.896 秒 |
| 1.1 背景解壓＋逐檔驗證＋準備 | 0.610 秒 | 9.765 秒 |
| 1.2 ZIP 大小 | 6,343,825 bytes | 76,313,410 bytes |
| 1.2 背景下載＋ZIP 驗證 | 0.365 秒 | 0.865 秒 |
| 1.2 背景解壓＋準備＋淘汰舊 pending | 0.633 秒 | 10.420 秒 |
| 要求重啟→1.2 主視窗可見 | 2.178 秒 | 2.041 秒 |
| 初始磁碟用量（含測試設定／狀態） | 22,732,956 bytes | 190,842,180 bytes |
| 1.1 備妥後 | 45,371,984 bytes | 381,346,911 bytes |
| 1.2 取代 1.1 pending 後 | 45,372,946 bytes | 381,410,072 bytes |
| 切換完成（保留回復版本） | 45,373,530 bytes | 381,410,656 bytes |

Launcher 本身為 89,088 bytes，含既有多尺寸圖示；不打包另一份 .NET runtime。

測試結果兩模式均通過：

- 保持 1.0 程序／視窗不退出，先準備 1.1 再準備 1.2，直接切換至 1.2，沒有啟動 1.1。
- 再查同一待更新版本沒有額外下載；舊 pending 資源目錄清理，磁碟未累積三套版本。
- 重複啟動根入口不建立第二個資料 owner；真正切換等待舊程序退出。
- 完整 ZIP、全部 payload 檔案雜湊、狀態、偏好／統計、自建檔保留；Full 實際載入 hash 目錄的 coreclr.dll。
- 另注入無效 PE 新版，真實 Launcher 啟動失敗後自動回到上一個可用程序與資料快照，再正常退出。

量測方式：背景兩個時間由 `Stopwatch` 的 Downloading／Preparing／Ready 狀態量測；網路 body bytes 由 loopback server 記錄。重啟時間從腳本發出與 UI 相同的 Launcher 重啟請求並關閉舊視窗，到新程序 MainWindowHandle 非零；不含使用者操作時間，不是渲染第一幀的精密追蹤，也不是實體人工按鍵計時。磁碟為檔案長度總和，不是 NTFS allocation／尖峰空間；準備過程短暫含 ZIP＋解壓資料，尖峰可能較高。

本機原始報告（不進 Git）：`artifacts/ota-integration-*/result.json`，最終跨版測試 Lite `3166065476d5401982b88a13e2910d49`、Full `d3a645e1ff5b416d8cd20602ae437b63`。另跑正常 1.0→1.1，Lite `b26270c8e63240e8be2987b7f8088f6b`、Full `3a1d42988b4d4262bdd09498b75ed3b6` 皆通過（重啟分別 2.126／2.045 秒）。

中途曾有失敗輪次：腳本以不允許 delete sharing 的讀取和 state 原子替換衝突；無效 PE 在 Windows loader 可能阻塞 Process.Start。已改成允許原子替換的讀取、等待 Launcher 完成確認再進下一步，並於 Launcher 預檢 PE header／x64 machine、抑制 OS 載入錯誤視窗，加入有界診斷紀錄。上表及四輪結果都是修正後重跑，沒有把失敗輪次寫成通過。

## 離線／UI 回歸

`--updates` 已驗證兩模式的數值版本比較、排除 draft／prerelease、準備不切換 Current、Latest Wins、取消舊下載、去重、指數退避、中斷後重試、新版失敗保留舊 Pending、ZIP hash／路徑穿越／重複檔／symlink／schema／清單不完整防護、跨程序鎖、原子 state 備份與未確認 Trial 的設定回復。

小型假包的數值保存在 `artifacts/ota-offline-metrics.json`，**不當成上表的真實 Portable 數值**。既有 UI／核心／系統音訊回歸與最終實作提交的驗證結果另外列在 PR。

## 尚未實測／不宣稱通過

- 新協定正式 GitHub Release 的 WAN 下載／公開發行：尚未執行，等待 PR 與 tag／草稿資產處理確認。
- 真實電台在背景完整 Full 下載時的人工聽音／長時間穩定：尚未執行；UI 假播放器只驗證狀態變動不停止或重開音訊。
- 實體斷電／磁碟損毀／磁碟滿、網路分享磁碟、各種安全軟體／唯讀路徑政策與實體多螢幕。
- 健康握手之後才發生的崩潰不會自動回復；此健康訊號不是音訊或完整應用功能的長期健康檢查。
