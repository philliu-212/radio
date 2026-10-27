Radio Portable Full — Windows x64

解壓縮完整資料夾，雙擊 Radio.exe 即可開啟。
已內含 .NET runtime，不需要另外安裝 .NET、SDK、mpv 或 FFmpeg。
請保留全部檔案，不要只複製 Radio.exe。

需求：Windows 10 2004／Windows 11 x64，具備 Windows 媒體元件及網路。
Windows N 版本仍需要系統的 Media Feature Pack；本包不會安裝或更改系統元件。

這是 self-contained 資料夾版，不是會把 runtime 解壓到暫存目錄的單一檔案。
比 Lite 版大，是因為把原本由系統提供的 .NET runtime 一起帶著走。
此包未內含第三方播放器或音訊解碼器，播放使用 Windows Media Foundation。
Hit FM 使用 Windows 相容播放路徑，其餘五台使用 Windows 純音訊模式。
附帶 runtime 的授權／通知檔保留在資料夾內。

操作：選台後按播放；停止會斷開來源，再次播放回到直播。
縮小會收進系統匣並繼續播放；雙擊系統匣圖示可還原。
關閉視窗會停止並退出。

本階段先驗證播放與封裝，UI／深色模式將在下一階段重整。
人工聽音、長時間穩定、睡眠喚醒／斷網恢復尚待驗證。
.NET 8 支援即將結束，正式長期使用版本需再升級至受支援的 runtime。
