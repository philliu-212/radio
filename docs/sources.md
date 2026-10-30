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
| `rfi-monde` | `https://www.rfi.fr/fr/direct-monde`（2026-10-28 核對） | 頁面 `direct_rfi_monde` 的官方公開來源 `https://rfimonde64k.ice.infomaniak.ch/rfimonde-64.mp3`，直接播放 MP3，不選 Afrique 或重播來源 |

## 邊界與限制

- 動態來源在播放前重新解析；飛碟與 RFI Monde 的固定公開入口直接重用，避免把首頁的暫時網路問題變成播放的前置依賴。短效 URL 只留在記憶體，測試報告只記錄 CDN 主機名，不把帶 token 的 URL 或 cookie 寫入檔案。
- 解析器只接受該電台預期的 HTTPS 串流主機與預設連接埠，失敗時不嘗試偽造簽章、使用代理或繞過限制。
- 好事官網目前由 HTTP 入口提供來源；HTTPS 在此次調查逾時。音訊串流本身仍要求 HTTPS。未來手機端若使用同一 HTTP 入口，需另評估平台安全政策；不預先關閉整個 App 的傳輸安全限制。
- POP、Hit FM 官方頁面提示最長播放約六小時。原型不宣稱能連續播放超過此限制；來源失效後需使用者手動重新播放。
- KISS 的官方頁面請求曾出現 403，另一次請求可正常取得；網路可達性不代表所有裝置／時間都能播放。拒絕回應時應正常報錯。
- 靜態頁面正規表示式及官方 API 均可能變動；應維護小型、可離線測試的解析器，而不是啟動瀏覽器抓整站。
- Windows 使用系統 HLS 引擎。`DesiredSeekableWindowSize` 是向系統提出的請求，不是整個程序記憶體的硬上限，也不代表所有來源都會遵守。
- 現行 `SystemAudioPlayer` 對 Hit FM 採 Windows MediaPlayer 相容路徑，其餘採 Media Engine `AUDIOONLY` 模式；不再提供 mpv。KISS 的來源可包含影像，不能把舊相容路徑的成本當成現行預設結果；傳輸是否只有音訊仍需另外量測，不從「畫面沒有影像」推論。
- Media Engine 的 `WAITING` 表示缺少下一段音訊而停播，不是一般背景收資料。某些 MP3 直播恢復時不會再次送出 `PLAYING`；僅在等待恢復期間處理原生 `TIMEUPDATE`，以播放時鐘確實前進且已有後續資料、未暫停／跳轉／結束為依據恢復「播放中」。單純 ready-state／來源預抓事件不代表已恢復；不新增輪詢 timer、重連或常駐 UI 時鐘。離線原生 WAV 測試與線上串流驗證分開記錄。
- RFI 不增加額外開播等待。3 秒預載在同資料對照未減少時鐘停頓，20 秒等待不符合使用者需求，原生 buffering-time 屬性也曾出現反例，這些候選均未保留。停頓仍由 [#43](https://github.com/philliu-212/radio/issues/43) 追蹤；狀態修正不代表音訊停頓已解決。

## Metadata 維護摘要

實作在 `src/Radio.Core/StationMetadata.cs`、`BestRadioSchedule.cs` 與 `OfficialProgramPages.cs`，不是每台都提供即時歌名。2026-10-30 依 [#61](https://github.com/philliu-212/radio/issues/61) 補查並接入非 KISS 電台：

| 電台 | 已採用的官方資料 | 顯示限制 |
| --- | --- | --- |
| 好事989 | `http://www.bestradio.com.tw/schedule989.htm` 的節目表 | 依台灣時間及週別計算節目、DJ、時段，標示「節目表」，不是即時 on-air 確認；代班可能不同，不猜 DJ 或歌名。 |
| KISS | `https://www.kiss.com.tw/templates/header_songlist_json.php?area=0` 的 `now_playing`；`https://www.kiss.com.tw/` 的 FM99.9 on-air 頁首 | 忠實區分「現在播放／即將播放」，驗證歌曲時間；頁首補上節目及 DJ 並檢查臺灣時間的節目時段。既有兩行顯示歌曲狀態及「歌手 ｜ DJ ｜ 節目」，無有效歌曲時顯示節目／DJ。頁首失敗保留有效歌曲；缺少／過期資訊留白，不承諾與聲音同步。不載入網頁腳本、圖片或音訊。 |
| 中廣流行網 | 上表的官方 ChannelInfoBat API | 依頻道識別取得節目／DJ／時段；依官方 TTL 與時段結束取較早期限。不是歌曲資訊。 |
| Hit FM 北部 | `https://www.hitoradio.com/newweb/onair_n_ajax_cname.php` | 將官方「不累DJ - Bibi趙之璧」類標籤分為節目與主持人；無明確 DJ 分隔格式則保留原文字，不猜分割點。HTML 當文字，不渲染。 |
| POP Radio | `https://www.pop917.com/` 的 `liveBox2` | 僅選北北基 FM91.7，讀取節目／主持人／台灣時段／NEXT；允許輪播相同副本，衝突或時段不符留白，不混入 FM90.7 或排行榜。 |
| 飛碟電台 | `https://www.uforadio.com.tw/` 的 `#stream` | 核對播放器為已知飛碟來源，只讀節目／主持人／台灣時段，不把宣傳區或排行榜當直播資訊。 |
| RFI Monde | `https://www.rfi.fr/fr/grille-des-programmes/monde` | 僅讀當日 UTC 日期、唯一官方 `En Direct` 標記的節目／主持人與可見 TU 開始時間，明確標示「節目表」；不從相鄰節目推論結束時間。可見 TU 與 HTML datetime offset 有不一致，因此不使用 datetime 推算；可見時間在未來或距今滿一小時即留白（保守新鮮度上限，不代表節目長度），跨 UTC 日期清除。403、版型變動或無有效標記也留白。 |

上述非 KISS 電台本次未確認可用的即時歌曲／歌手來源。Hit FM 的 `search.php` 是需姓名、email 與驗證碼、由人員回覆的三日內查歌表單，不是即時歌單 API；POP 的歌單連結目前被註解。RFI 直播頁的 emission.title 為空，同頁 podcasts 為重播，不能冒充直播。

時段與 NEXT 共用既有第二行，長文字可由既有 tooltip 看完整內容，不改 UI 字級或版型。節目／時段不承諾與音訊同步。POP、飛碟時段支援台灣時間跨日，於時段結束清除。

線上文字抽查指令：`dotnet run --project tests/Radio.Core.Tests -c Release -- --metadata-live`（排除 KISS、不開音訊、不保存帶 token 的 URL）。2026-10-30 抽查 POP、飛碟、中廣、好事與 Hit FM 均取得有效節目／主持人；RFI 回傳空值，未列為線上成功。研究工具可讀 RFI 節目表，但本機 PowerShell 曾遭拒絕，不能以研究工具可達代替播放器環境可用。

- 僅對實際播放中的受支援電台在視窗可見時查詢；切台、停止、最小化會取消額外查詢，舊回應不能套到新電台。
- 查詢逾時、格式變動、HTTP 拒絕或 TTL 到期只清除附加資訊，不影響音訊；節目表快取也須依目前時間重新計算時段。
- 不另開音訊串流做 metadata 偵測、不執行網站腳本、不為文字查詢引入瀏覽器或繞過 403／驗證碼。文字回應上限 1 MiB（RFI 有大量 inline CSS），其他新增頁面解析仍限 256 KiB；不下載連結資源。
- 舊抽樣與實驗細節保留於 Git 歷史，不作為目前直播內容或穩定性的證據。離線測試見 `tests/Radio.Core.Tests/MetadataChecks.cs`。

## 參考

- [TWeFM 關於與資料來源](https://twefm.com/about)
- [TWeFM 使用條款](https://twefm.com/terms)
- [Windows MediaPlayer](https://learn.microsoft.com/en-us/windows/apps/develop/media-playback/play-audio-and-video-with-mediaplayer)
- [Windows AdaptiveMediaSource](https://learn.microsoft.com/en-us/uwp/api/windows.media.streaming.adaptive.adaptivemediasource)
