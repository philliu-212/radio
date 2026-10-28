Radio Portable Lite — Windows x64

解壓縮整個資料夾，雙擊 Radio.exe 即可開啟。
不用安裝本程式、不需要 mpv／FFmpeg，也不需要 SDK 或命令列。
請保留資料夾內全部檔案，不要只複製 Radio.exe。

需求：
- Windows 10 2004／Windows 11 x64，具備 Windows 媒體元件。
- Microsoft .NET 8 Desktop Runtime（x64），不是只有一般 .NET Runtime。
  缺少 runtime 時，Windows/.NET 啟動器會提示；本程式不會自行安裝。
- 可連接電台官方來源的網路。

這是依賴系統 runtime 的輕量 portable 包，不是內含整套 runtime 的獨立包。
「免安裝本程式」不代表所有電腦都完全不需要準備環境。

操作：
- 選台後按播放圖示（或在清單按 Enter／Space）；點擊只選取，不會自動播放。
- 播放中按方形停止；選取另一台後按播放可切台。
- 「停止」會斷開播放來源；重新播放回到直播。
- 縮小收進系統匣並繼續播放，雙擊系統匣圖示可還原。
- 關閉視窗會停止播放並退出。

Direction C：550 × 270 DIP 橫向介面，支援深／淺色切換。
主題偏好保存在同資料夾的 radio.settings.json；唯讀資料夾則僅當次有效。
有官方資料時顯示節目／DJ 或預告歌曲；縮到系統匣後停止額外查詢。
系統匣選單可關閉資訊查詢。無資料時不顯示假內容。
其中 Hit FM 使用 Windows 相容播放路徑，其餘五台使用 Windows 純音訊模式。
不包含錄音、時移、背景分析、追蹤或自動更新。
本機驗證為短時間靜音測試，不代表已完成人工聽音與長時間穩定驗證。
