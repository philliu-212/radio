# 官方電台來源調查

調查日期：2026-10-27。TWeFM 僅作為目錄研究參考，執行時不呼叫 TWeFM API 或代理。

| ID | 官方入口／流程 | 原型解析方式 |
| --- | --- | --- |
| `best-989` | `http://www.bestradio.com.tw/` | 擷取 989 的 `RA000013` HLS，保留官方生成的 query string |
| `kiss-999` | `https://www.kiss.com.tw/radio_hq.php?radio_id=156` 內嵌 `/test/hichannel2.php?api=1` | 從官方嵌入頁擷取 `RA000040` HLS |
| `pop-917` | `https://www.pop917.com/liveStream.aspx?id=1` | 初始化頁面 session，再 POST `https://www.pop917.com/ajax.aspx`，表單為 `channelID=1&action=getLIVEURL` |
| `ufo-921` | `https://www.uforadio.com.tw/` 公開 `<source>` | 保存經核對的固定 Revma 公開入口 `https://stream.rcs.revma.com/em90w4aeewzuv`，不在每次收聽前下載整個首頁 |
| `bcc-ilike` | `https://www.bcc.com.tw/onAir.asp?nid=2` 使用的官方 API | GET `https://www.bcc.com.tw/webapi/BCCRadioWebAPI/ChannelInfoBat`，依名稱「中廣流行網」選取 `iosStream`，不依陣列序號選台 |
| `hit-north` | `https://www.hitoradio.com/newweb/nowonaircount_new.php?block=0&area=6` | 初始化頁面 session，再 POST `https://www.hitoradio.com/newweb/hichannel.php`，表單為 `channelID=1&action=getLIVEURL` |

## 邊界與限制

- 動態來源在播放前重新解析；飛碟的固定公開入口直接重用，避免把首頁的暫時網路問題變成播放的前置依賴。短效 URL 只留在記憶體，測試報告只記錄 CDN 主機名，不把帶 token 的 URL 或 cookie 寫入檔案。
- 解析器只接受該電台預期的 HTTPS 串流主機與預設連接埠，失敗時不嘗試偽造簽章、使用代理或繞過限制。
- 好事官網目前由 HTTP 入口提供來源；HTTPS 在此次調查逾時。音訊串流本身仍要求 HTTPS。未來手機端若使用同一 HTTP 入口，需另評估平台安全政策；不預先關閉整個 App 的傳輸安全限制。
- POP、Hit FM 官方頁面提示最長播放約六小時。原型不宣稱能連續播放超過此限制；來源失效後需使用者手動重新播放。
- KISS 的官方頁面請求曾出現 403，另一次請求可正常取得；網路可達性不代表所有裝置／時間都能播放。拒絕回應時應正常報錯。
- 靜態頁面正規表示式及官方 API 均可能變動；應維護小型、可離線測試的解析器，而不是啟動瀏覽器抓整站。
- Windows 使用系統 HLS 引擎。`DesiredSeekableWindowSize` 是向系統提出的請求，不是整個程序記憶體的硬上限，也不代表所有來源都會遵守。
- KISS 的 HLS 在實驗中回報 `AudioOnlyPlayback=false`、影像尺寸 `640x360`。即使不顯示畫面，原生引擎仍有額外成本。嘗試將 `MediaPlaybackItem.VideoTracks.SelectedIndex` 設為 `-1` 會被本機 Windows 引擎以 `0x80070057` 拒絕，該實驗程式已移除。不要聲稱目前已停用影像解碼或只下載音訊。

## 參考

- [TWeFM 關於與資料來源](https://twefm.com/about)
- [TWeFM 使用條款](https://twefm.com/terms)
- [Windows MediaPlayer](https://learn.microsoft.com/en-us/windows/apps/develop/media-playback/play-audio-and-video-with-mediaplayer)
- [Windows AdaptiveMediaSource](https://learn.microsoft.com/en-us/uwp/api/windows.media.streaming.adaptive.adaptivemediasource)
