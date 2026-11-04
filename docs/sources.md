# 官方電台來源調查

調查日期：2026-10-27。TWeFM 僅作為目錄研究參考，執行時不呼叫 TWeFM API 或代理。

| ID | 官方入口／流程 | 原型解析方式 |
| --- | --- | --- |
| `best-989` | `http://www.bestradio.com.tw/` | 擷取 989 的 `RA000013` HLS，保留官方生成的 query string |
| `kiss-999` | `https://www.kiss.com.tw/radio_hq.php?radio_id=156` 內嵌 `/test/hichannel2.php?api=1` | 從官方嵌入頁擷取 `RA000040` HLS |
| `kiss-999` 備援 | `https://www.twitch.tv/livekiss` | 匿名取得 Twitch 播放授權及 usher HLS 清單，僅選 `audio_only`，不載入 Twitch 網頁或保存短效 token |
| `pop-917` | `https://www.pop917.com/liveStream.aspx?id=1` | 初始化頁面 session，再 POST `https://www.pop917.com/ajax.aspx`，表單為 `channelID=1&action=getLIVEURL` |
| `ufo-921` | `https://www.uforadio.com.tw/` 公開 `<source>` | 保存經核對的固定 Revma 公開入口 `https://stream.rcs.revma.com/em90w4aeewzuv`，不在每次收聽前下載整個首頁 |
| `bcc-ilike` | `https://www.bcc.com.tw/onAir.asp?nid=2` 使用的官方 API | GET `https://www.bcc.com.tw/webapi/BCCRadioWebAPI/ChannelInfoBat`，依名稱「中廣流行網」選取 `iosStream`，不依陣列序號選台 |
| `hit-north` | `https://www.hitoradio.com/newweb/nowonaircount_new.php?block=0&area=6` | 初始化頁面 session，再 POST `https://www.hitoradio.com/newweb/hichannel.php`，表單為 `channelID=1&action=getLIVEURL` |
| `rfi-monde` | `https://www.rfi.fr/fr/direct-monde`（2026-10-28 核對） | 頁面 `direct_rfi_monde` 的官方公開來源 `https://rfimonde64k.ice.infomaniak.ch/rfimonde-64.mp3`，直接播放 MP3，不選 Afrique 或重播來源 |
| `esradio` | `https://esradio.libertaddigital.com/`、`directo.html`（2026-11-02 核對） | 直接播放同一官方 Flumotion 頻道的 `https://libertaddigital-radio-live1.flumotion.com/libertaddigital/ld-live1-high.mp3`；Spain、Spanish `es`、News / Talk |

## 邊界與限制

- 動態來源在播放前重新解析；飛碟與 RFI Monde 的固定公開入口直接重用，避免把首頁的暫時網路問題變成播放的前置依賴。短效 URL 只留在記憶體，測試報告只記錄 CDN 主機名，不把帶 token 的 URL 或 cookie 寫入檔案。
- 解析器只接受該電台預期的 HTTPS 串流主機與預設連接埠，失敗時不嘗試偽造簽章、使用代理或繞過限制。
- 好事官網目前由 HTTP 入口提供來源；HTTPS 在此次調查逾時。音訊串流本身仍要求 HTTPS。未來手機端若使用同一 HTTP 入口，需另評估平台安全政策；不預先關閉整個 App 的傳輸安全限制。
- POP、Hit FM 官方頁面提示最長播放約六小時。原型不宣稱能連續播放超過此限制；來源失效後需使用者手動重新播放。
- KISS 的官方頁面請求曾出現 403，另一次請求可正常取得；網路可達性不代表所有裝置／時間都能播放。拒絕回應時應正常報錯。
- KISS 開播優先使用 HiNet；解析、引擎開播失敗或單次開播超過 30 秒時，只嘗試一次 Twitch 備援。整體上限 70 秒，停止／切台會取消，不無限重試；播放成功後的中途斷線不自動換來源，再按播放會重新由 HiNet 開始。其他電台不使用此備援。
- Twitch 僅使用公開匿名播放流程，接受 `*.ttvnw.net`／`*.live-video.net` 的 HTTPS 純音訊 HLS。離線、拒絕、需要登入、限流、缺少音軌或格式改變都正常報錯，不繞過限制、不移除廣告。公開 web client ID／未版本化 API 可能變更，無可用純音訊時不退回含影片音軌。
- #94 線上驗證：本機 .NET 一般開播實際切換至 Twitch；另以模擬主來源失敗驗證相同 UI 路徑，兩次 Windows 系統音訊均靜音觀察 20 秒，播放時鐘各前進約 20.1 秒。這不是人工聽音、長時間穩定性或所有網路環境的驗收。

- 靜態頁面正規表示式及官方 API 均可能變動；應維護小型、可離線測試的解析器，而不是啟動瀏覽器抓整站。
- Windows 使用系統 HLS 引擎。`DesiredSeekableWindowSize` 是向系統提出的請求，不是整個程序記憶體的硬上限，也不代表所有來源都會遵守。
- 現行 `SystemAudioPlayer` 對 Hit FM 採 Windows MediaPlayer 相容路徑，其餘採 Media Engine `AUDIOONLY` 模式；不再提供 mpv。KISS 的來源可包含影像，不能把舊相容路徑的成本當成現行預設結果；傳輸是否只有音訊仍需另外量測，不從「畫面沒有影像」推論。
- Media Engine 的 `WAITING` 表示缺少下一段音訊而停播，不是一般背景收資料。某些 MP3 直播恢復時不會再次送出 `PLAYING`；僅在等待恢復期間處理原生 `TIMEUPDATE`，以播放時鐘確實前進且已有後續資料、未暫停／跳轉／結束為依據恢復「播放中」。單純 ready-state／來源預抓事件不代表已恢復；不新增輪詢 timer、重連或常駐 UI 時鐘。離線原生 WAV 測試與線上串流驗證分開記錄。
- RFI 不增加額外開播等待。3 秒預載在同資料對照未減少時鐘停頓，20 秒等待不符合使用者需求，原生 buffering-time 屬性也曾出現反例，這些候選均未保留。停頓仍由 [#43](https://github.com/philliu-212/radio/issues/43) 追蹤；狀態修正不代表音訊停頓已解決。

### KISS 備援驗證

後兩個指令會連線；`--kiss-live` 靜音、不寫使用者設定：

```powershell
dotnet run --project tests/Radio.Windows.Tests -c Release -- --kiss-backup
dotnet run --project tests/Radio.Windows.Tests -c Release -- --kiss-live
dotnet run --project src/Radio.Windows -c Release -- --probe --station kiss-999 --fallback --seconds 20 --output artifacts/kiss-fallback-probe.json
```

`--kiss-backup` 是離線 UI fixture；`--kiss-live` 包含實際主來源解析，以及模擬主來源故障後的真實 Twitch 播放；`--probe --fallback` 僅獨立量測備援，不驗證自動切換。診斷不輸出完整播放網址或 token，產物不提交。

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
| RFI Monde | `https://www.rfi.fr/fr/grille-des-programmes/monde` | 核對 Monde canonical、頁面日期與唯一官方 `En Direct` 標記，取得節目／主持人；採用 `<time datetime>` 的完整 ISO 時間與明確 offset 換算 UTC，不把可見「TU」文字直接當 UTC。頁面日期依該 offset 比對目前日期及節目日期；在實際未開始、日期過期、403、版型變動或無有效標記時留白。標示「節目表」，不從相鄰節目或自訂一小時推論結束，最多保留 90 秒並於頁面當地午夜清除，沿用可見時查詢的既有更新週期。 |

上述非 KISS 電台本次未確認可用的即時歌曲／歌手來源。Hit FM 的 `search.php` 是需姓名、email 與驗證碼、由人員回覆的三日內查歌表單，不是即時歌單 API；POP 的歌單連結目前被註解。RFI 直播頁的 emission.title 為空，同頁 podcasts 為重播，不能冒充直播。

時段與 NEXT 共用既有第二行，長文字可由既有 tooltip 看完整內容，不改 UI 字級或版型。節目／時段不承諾與音訊同步。POP、飛碟時段支援台灣時間跨日，於時段結束清除。

線上文字抽查指令：`dotnet run --project tests/Radio.Core.Tests -c Release -- --metadata-live`（排除 KISS、不開音訊、不保存帶 token 的 URL）；單獨驗證 RFI 可用 `--metadata-rfi-live`，資料空白或請求失敗時回傳非零 exit code。2026-10-30 抽查 POP、飛碟、中廣、好事與 Hit FM 均取得有效節目／主持人；同日 09:39 UTC，修正後的本機 .NET provider 也成功取得 RFI「8 milliards de voisins」、主持人 Emmanuelle Bastide、09:10 UTC 起。

RFI 初次回傳 null 的解析問題已修正：真實頁面 live 項目顯示 `10:10 TU`，但 machine-readable datetime 為 `2026-10-30T10:10:30+01:00`（09:10:30 UTC）。舊程式忽略 offset，把顯示文字當 UTC，誤判為未來；且自行設定的一小時上限可能丟棄有效 live marker，現已移除。真實回應精簡 fixture 重現舊版失敗，並覆蓋時區／當地日期跨 UTC 午夜／實際開始邊界與缺 offset。網站確實有有效資料，不能把初次 null 說成不存在；網路抽查另曾逾時或遭拒，仍須正常處理暫時失敗，不宣稱永久可達或與音訊同步。

- 僅對實際播放中的受支援電台在視窗可見時查詢；切台、停止、最小化會取消額外查詢，舊回應不能套到新電台。
- 查詢逾時、格式變動、HTTP 拒絕或 TTL 到期只清除附加資訊，不影響音訊；節目表快取也須依目前時間重新計算時段。
- 不另開音訊串流做 metadata 偵測、不執行網站腳本、不為文字查詢引入瀏覽器或繞過 403／驗證碼。文字回應上限 1 MiB（RFI 有大量 inline CSS），其他新增頁面解析仍限 256 KiB；不下載連結資源。
- 舊抽樣與實驗細節保留於 Git 歷史，不作為目前直播內容或穩定性的證據。離線測試見 `tests/Radio.Core.Tests/MetadataChecks.cs`。

## esRadio 官方來源查核（2026-11-02，#83／#84）

- 官方直播頁 `<audio>` 列出 `ld-live1-med.aac` 與 `ld-live1-low.mp3`；採用同一 `libertaddigital-radio-live1.flumotion.com/libertaddigital/` 頻道的 high MP3。FFmpeg 實際解碼 high MP3 90 秒、low MP3 60 秒成功且有非零 PCM 音訊，分別辨識為 128／64 kbps、44.1 kHz stereo；HTTP `icy-br` 卻皆聲稱 56，音質以解碼資訊為準。
- 備用官方 MP3：`https://libertaddigital-radio-live1.flumotion.com/libertaddigital/ld-live1-low.mp3`。目前沒有多 URL 自動備援機制，因此只記錄並實測備用，不為本次重構引擎或增加自動切換。AAC `ld-live1-high.aac` HTTP 200，僅作來源／ICY 抽查，未宣稱已驗收 AAC 播放。
- 官方 JS `https://s1.staticld.com/js/299/210318.min.js` 使用 `https://s1.staticld.com/rt/programa-actual-radio.json` 更新全國站導覽列 `#directoesradio`，因此直接沿用此結構化來源，不解析地方分台節目表，也不由文章猜測直播內容。JSON 的 `nombre`、`presentadores`、`hora_inicio`、`hora_final`、`siguiente.nombre` 可取得；忽略 `audios`（Podcast／文章），不當歌曲。
- 2026-11-03 查核（#89）：官方回傳 **Es Música**、Madrid **02:00–04:30**，`url: null`、`presentadores` 為空。節目介紹頁不是顯示名稱的必要條件；目前與下一節目允許缺少／null／空白網址，有提供非空網址時仍驗證官方 HTTPS 網址。名稱與有效時段照官方顯示，缺主持人留白；時段與過期檢查不放寬。
- 本機 .NET provider 於約 09:26 UTC 實際取得 **Es la Mañana de Federico**、**Federico Jiménez Losantos**、Madrid **06:00–12:00**、下一節目 **Kilómetro Cero**。只代表當次抽查，不是寫死顯示內容。
- 主持人交叉核對官方節目頁的 `author`／description／主持人區塊：`es-la-manana-de-federico/` → Federico Jiménez Losantos；`en-casa-de-herrero/` → Luis Herrero；`la-noche-de-cuesta/` → Carlos Cuesta；`la-trinchera-de-llamas/` → Manuel Llamas；`informativos.html` → Juan Pablo Polvorinos。實作仍優先採即時 JSON 的主持人，缺少就留白，不建立歷史人物對照表。
- 官方 `programacion.html` 有平日／週末時段，亦含 Madrid 地方節目及重播；不將其 CSS 格子當作可靠直播計算來源。平日／週末節目由官方 current API 選取；本機只以 `Europe/Madrid`（含夏令時間）驗證時段，支援跨午夜，拒絕無效時段與超時選項。JSON 無日期，無法獨立證明伺服器從未回傳同時段舊資料；不宣稱與聲音逐秒同步。
- 沿用既有可見且播放中才查詢／最長 60 秒輪詢、90 秒 metadata 有效上限及切台取消機制，不增加 timer 或快取架構。有效期限取 90 秒與節目結束較早者；資料失敗沿用空白，播放仍獨立運作。下一節目使用既有第二行。
- high MP3、low MP3、high AAC 皆 HTTP 200、`icy-metaint: 1024`；分別取樣約 20.4 秒，共讀 463／232／352 個 ICY block，未取得非空 metadata 或 `StreamTitle`。不能據此保證永遠沒有，但本次沒有可可靠辨識的歌名／節目標題，因此不把 ICY 接入歌曲欄位，執行時也不另開第二條音訊連線探測。
- 官方 Logo 與本次不改 UI 的限制見 [電台資產說明](../src/Radio.Windows/Assets/Stations/README.md)。

### 狀態卡片地區與頻率（#86）

使用者核准將 `Spain Madrid`／`France` 以灰色小字接在台名後方，頻率列保留大字 `99.1`＋小字 `FM`／大字 `WEB`，不增加行數或移動節目欄位。官方 [節目表](https://esradio.libertaddigital.com/programacion.html) 明列「Kilómetro Cero - Madrid 99.1」及另一頻率「Con ánimo de lucro - Madrid 105.7」；先前同一 `ld-live1` 全國直播頁使用的 current JSON，其下一節目為 Kilómetro Cero。以此交叉確認 Madrid 99.1 識別，不選 105.7，也不宣稱地方 FM 與網路直播每一刻完全同步。

目錄新增 `city: Madrid`、`broadcastFrequency: 99.1` 供卡片使用，不覆寫原本空白的 `frequency` 或任何 URL／語言。RFI Monde 沿用已知國家 France，不猜地方城市與 FM 頻率。國家／城市不是播放來源選擇條件；不增加任何備援或自動切台。

明確啟用線上測試（不含於離線套件、不錄音、不寫使用者設定）：

```powershell
dotnet run --project tests/Radio.Core.Tests -c Release -- --esradio-live
dotnet run --project tests/Radio.Windows.Tests -c Release -- --esradio
```

前者讀取真實節目 JSON 並分別抽查三個串流的 ICY；後者用正式 `SystemAudioPlayer` 靜音觀測 high MP3 120 秒、low MP3 60 秒、切 RFI 20 秒再回 esRadio 30 秒，檢查播放事件、時鐘持續前進與錯誤。這不是實際聽感、數小時穩定性、斷網恢復或實體多螢幕驗收；執行結果應另見 PR 紀錄。初次 low 30 秒量測僅前進 16.7 秒，連續性檢查失敗（無引擎錯誤），因此延長備用來源觀測並讓診斷在個別連續性失敗後繼續測切台；不放寬檢查門檻、不把失敗刪除。

延長觀測實際結果：high 120.1 秒前進 116.7 秒，low 60 秒前進 46.7 秒，切 RFI 20 秒前進 16.0 秒、再回 high 30 秒前進 26.4 秒，皆無引擎錯誤；high／RFI／回切符合短測門檻，**low 仍未通過時鐘連續性門檻，因此整個線上診斷回傳失敗**。最長停滯取樣為 high 3 秒／low 8 秒，不將時鐘取樣直接描述為已聽見的靜音。正式目錄維持 high MP3，不啟用未驗收的自動備援，也不為備用問題改播放引擎。

## 參考

- [TWeFM 關於與資料來源](https://twefm.com/about)
- [TWeFM 使用條款](https://twefm.com/terms)
- [Windows MediaPlayer](https://learn.microsoft.com/en-us/windows/apps/develop/media-playback/play-audio-and-video-with-mediaplayer)
- [Windows AdaptiveMediaSource](https://learn.microsoft.com/en-us/uwp/api/windows.media.streaming.adaptive.adaptivemediasource)
