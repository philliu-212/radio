Radio Portable Full — Windows x64

解壓縮完整資料夾，雙擊 RadioMini.exe 即可開啟。
已內含 .NET runtime，不需要另外安裝 .NET、SDK、mpv 或 FFmpeg。
請保留全部檔案，不要只複製 RadioMini.exe。

需求：Windows 10 2004／Windows 11 x64，具備 Windows 媒體元件及網路。
Windows N 版本仍需要系統的 Media Feature Pack；本包不會安裝或更改系統元件。

這是 self-contained 資料夾版，不是會把 runtime 解壓到暫存目錄的單一檔案。
比 Lite 版大，是因為把原本由系統提供的 .NET runtime 一起帶著走。
此包未內含第三方播放器或音訊解碼器，播放使用 Windows Media Foundation。
Hit FM 使用 Windows 相容播放路徑，其餘使用 Windows 純音訊模式。
附帶 runtime 的授權／通知檔保留在資料夾內。

操作：用 + 管理收藏電台；點選電台立即開播／切換。
我的電台按成功播放次數排列，每頁五台，支援細捲動條／滾輪／鍵盤。
音量可直接點擊／拖曳調整，並記住上次音量。
播放中按方形停止會斷開來源，再次播放回到直播；不提供暫停時移。
縮小會收進系統匣並繼續播放；雙擊系統匣圖示可還原。
關閉視窗會停止並退出。
有正式新版時右上角顯示下載箭頭；點擊後下載同類型 Full 包、驗證並重啟，保留設定。下載期間繼續播放。
啟動及每小時背景檢查 GitHub Release；更新需要可寫資料夾及 Windows PowerShell。失敗可重試或手動下載，記錄位於 %TEMP%/RadioMini-update-*/result.txt。

極簡橫向介面，支援深／淺色切換。啟動播放策略等操作以該版本的專案 README／Release Notes 為準。
主題、音量、收藏與播放次數保存在同資料夾的 radio.settings.json；唯讀資料夾則僅當次有效。
有官方資料時顯示節目／DJ 或預告歌曲；縮到系統匣後停止額外查詢。
好事989 顯示依官方節目表推定的節目／DJ，非即時播出確認；歌名尚無可靠來源。
系統匣選單可關閉資訊查詢。無資料時不顯示假內容。
人工聽音、長時間穩定、睡眠喚醒／斷網恢復尚待驗證。
.NET 8 支援即將結束，正式長期使用版本需再升級至受支援的 runtime。
