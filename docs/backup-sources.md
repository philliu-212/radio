# 備援來源掃描與穩定度機制（#94／#96／#97／#104）

2026-11-04 掃描目前全部八台；不把搜尋結果、第三方播放器入口、不同地區分台或節目重播當作已驗證備援。此頁是當次觀測，不保證往後每個端點持續可用。

## 共用機制

- 首輪依 **主來源 → 依目錄順序的備援**；後續輪次優先使用本次收聽中已證明較穩的來源，不以碼率／音質排名。每個來源每輪最多一次，最多三個備援。停止再播放／手動重新播放會清除本次來源紀錄，重新依目錄順序開始。
- 同一輪另對已解析的完整串流 URL 做記憶體去重：像中廣固定入口若與剛才已開播的 API 結果相同，就跳過，不以「備援」名義重新開同一網址；只有 API 解析失敗而尚未拿到串流時，它才可避開 API 提供幫助。手動重播也清除此去重集合。
- 所有電台都監測，包括只有單一來源的電台。每秒讀一次播放時鐘；連續 **8 秒**沒有至少 0.05 秒的進展，或最近 **30 秒累積損失 12 秒**播放進度，視為持續不穩。短暫緩衝不立即換源；不只依賴引擎的 Playing／Buffering 文字狀態。
- 開播解析／播放失敗、開播逾時、播放中 Failed（含直播結束）或上述持續不穩時，切下一來源。單一來源或全數用盡時保留收聽意圖，顯示「等待自動重連」，以 **15、30、60、120、300 秒**退避；之後每 300 秒依穩定度排序重新解析一輪，清除本輪 URL 去重，不形成快速無限迴圈。時間從本輪失敗後起算；背景／隱藏時也適用。狀態提示保留失敗原因與等待秒數，不新增 UI 版型。
- 播放時鐘持續健康 **2 分鐘**只恢復初始退避，**不清除本輪已嘗試來源**；至少健康 **10 分鐘**才允許下次中斷時立即開新輪，先重連長播來源。這避免每 3 分鐘斷線的主來源反覆重試自己，永遠不前進備援。最近 30 秒損失達 2 秒或連續 3 秒無進展會重新計算健康時間；睡眠、UI 長時間未排程、時鐘跳動不算健康播放。單次 Playing 回呼不能重設退避。
- 排序依每個來源**最近一次嘗試**中最長的連續健康時間，由長到短，同分沿用目錄順序；未有證據者為 0，不預設低碼率一定穩定。曾播放 1 小時的低碼率會優先於只播 3 分鐘便斷線的高碼率；正在健康播放時絕不為升級音質而切換。若原本可靠的來源後來很快失敗，以新結果替換舊證據，避免永久黏住退化來源。這些資料僅存記憶體，不增加播放統計、不寫 portable 設定或跨次保存。
- 自動重連不增加收聽次數；初次開播一直失敗者於真正成功時才計一次。停止、換台、移除正在收聽的電台及退出會取消等待／排隊重連；等待時播放按鈕維持「停止」，可隨時取消。音量與靜音沿用既有播放器設定。不繞過 HTTP 存取限制，也不保證已停播或網路不可用的來源一定恢復。
- 有備援者每個來源開播上限 30 秒；一次開播切換工作總預算為剩餘來源數 × 30 秒 + 10 秒。無備援者保留原本 40 秒開播上限。成功播放後持續監測，不限制收聽時間。
- 停止／切台／退出會取消開播與已排程的換源；自動換源不增加收聽次數、不更換電台識別，保留音量／靜音。手動新一輪真的 Playing 才再次計次。
- 隱藏／最小化仍監測，與可見 UI／metadata 動畫分開；停止／退出釋放 timer。睡眠／UI 長時間未排程（取樣間隔 > 5 秒）及時間軸跳動會重設判斷基準，不直接歸咎來源故障。
- 僅改來源編排與穩定度判斷，沒有換播放器引擎、代理、錄音、預載等待或登入／地區限制繞過。不同 URL 仍可能共用同一機房，全部不穩時不能憑空恢復聲音。

### 長時間中斷恢復（#104，2026-11-05）

- v0.5.5 診斷：11/4 18:39 已切至最後 AAC 備援，23:02 收到直播結束後停在 Failed，直到隔日上午仍未重連（UTC+8）。當晚並未重新測試先前兩個來源，因此不能把「本輪耗盡」當成「當下所有來源同時故障」。結束事件無底層網路錯誤碼，無法歸因為電台或本機網路。
- 離線可控時鐘測試涵蓋：最後備援健康播放 4 小時後結束時優先重連該備援、15/30/60/120/300/300 秒退避、短暫 Playing 不重設退避、健康 2 分鐘只恢復退避、3 分鐘主來源前進備援、低碼率健康 1 小時不主動升級且重連優先、來源退化仍前進其他來源、單來源重連、重連不重複計次，以及停止／換台／退出使等待中／已排隊重連失效。模擬時鐘不等於實際播放 1／4 小時。
- 初版 #104 的 `dotnet run --project tests/Radio.Windows.Tests -c Release -- --reconnect-live`：模擬首輪解析全部失敗，再以真實計時器／官方解析器／Windows 音訊恢復 esRadio。視窗隱藏時約 15.3 秒重新嘗試主來源，28.8 秒時狀態 Playing、播放時鐘前進 10.1 秒，主來源解析 2 次、首輪備援 2 次、成功收聽計次 1；全程靜音、不寫 portable 設定。後續補上穩定度優先排序，並非用此短測證明哪個來源能長播。
- 穩定度優先調整後再次執行 `--reconnect-live`：約 16.2 秒重新嘗試，29.8 秒時 Playing、播放時鐘前進 10.3 秒，主來源 2 次／首輪備援 2 次／計次 1。建置、核心、完整 UI（含 3 分鐘／1 小時模擬）及系統音訊測試再次通過；未實際連聽 1 小時比較碼率。
- `--stability-live`：esRadio 觀測 120.1 秒，主來源解析 1 次、未切備援、結束時 Playing；RFI 觀測 120.3 秒，約 37.2 秒切至備援索引 0、結束時 Playing。各台計次 1，第一輪來源切換有界；這不是持續音質／整夜穩定保證。
- 此線上短測不是實體斷網恢復、可聽音質或整夜播放驗收；實際跨夜穩定度仍需後續觀測。正常播放狀態仍不新增 Tooltip，只有等待重連時顯示原因／等待秒數。

設定在 `src/Radio.Core/stations.json` 的 `backupSources`，不是在 UI 依電台 ID 硬編來源。共用 `IStreamResolver.ResolveFallbackAsync(..., index)` 與 `MainForm` 編排；時鐘政策位於 `PlaybackStability.cs`，可離線控制時間驗證。

## 掃描結果

| 電台 | 接入／候選 | 判斷與限制 |
| --- | --- | --- |
| KISS | 接入 [Twitch livekiss](https://www.twitch.tv/livekiss) `audio_only` | 與 HiNet 不同平台；每次匿名取得新授權，不保存 Twitch 短效網址，無純音訊／離線／拒絕時正常失敗。 |
| 中廣流行網 | 接入 `https://stream.rcs.revma.com/s1zttsg3qtzuv` | 當次[官方 API](https://www.bcc.com.tw/webapi/BCCRadioWebAPI/ChannelInfoBat) 的流行網 `iosStream`，可避開官網 API 故障，**與主串流同一 Revma 入口，不是獨立 CDN**。官方[舊播放器](https://www.bcc.com.tw/stream.html?c=0)仍使用該 API。未加入其他中廣頻道或 HTTP/IP Android 來源。 |
| RFI Monde | 接入 `https://rfiafrique64k.ice.infomaniak.ch/rfimonde-64.mp3` | 同 Infomaniak 的替代 hostname，**掛載路徑仍是 Monde，絕不是 `/rfiafrique-64.mp3`**。不是官方直播頁列出的第二來源；候選來自公開電台目錄，另以 HTTP `icy-name: RFI monde 64a` 與同時 30 秒 Chromaprint 交叉確認內容（220 frames 重疊，最佳平均約 1.514 mismatch bits/frame）。同供應商，不能當獨立機房；實測仍有嚴重停頓，#43 未解決。 |
| esRadio | 接入 low MP3 → med AAC | [官方直播頁](https://esradio.libertaddigital.com/directo.html)直接列出的 `ld-live1-low.mp3`、`ld-live1-med.aac`，與主來源同 Flumotion／`ld-live1` 全國頻道，不是獨立供應商。都有實際解碼及 Windows 開播，但當日也有連續性失敗，不宣稱穩定。 |
| 好事989 | 暫未加入可用備援 | 官方 Facebook 曾[公告 Twitch newbestradio](https://www.facebook.com/best989/posts/10156219922512644/)，但此次授權後的 usher 清單回 404，無可播放直播。官網現有四個 RA 編號含不同地區；只保留台北 RA000013，不以其他分台替代。公開目錄另有 HTTP/IP 舊 HLS，不降低 HTTPS 限制。 |
| POP 台北91.7 | 暫未加入可用備援 | [官方收聽頁](https://www.pop917.com/radio.aspx)仍指向現用 `liveStream.aspx?id=1`；id=2 是桃竹苗90.7，不能冒充台北備援。官方 YouTube 候選 [FeVa5INvdkQ](https://www.youtube.com/watch?v=FeVa5INvdkQ) 當次是已結束的專訪，不是全天候直播。 |
| 飛碟92.1 | 暫未加入可用備援 | [官網](https://www.uforadio.com.tw/)只有現用 Revma `em90w4aeewzuv`。公開目錄的 n10 URL 仍是相同 Revma 掛載點，不把固定邊緣節點當獨立來源。舊 `cast.uforadio.com.tw:8000/ufo` 當次無法連線；官網 YouTube handle 連結回 404，搜尋到的其他官方影片多為節目，未確認全天同台直播。 |
| Hit FM北部107.7 | 暫未加入可用備援 | [官方北部頁](https://www.hitoradio.com/newweb/onair_n_ajax.php)仍使用現有 HLS 流程。搜尋到官方 YouTube [ScmZei0U1bE](https://www.youtube.com/watch?v=ScmZei0U1bE) 實際 player response 為 UNPLAYABLE、isLiveNow=false，2022-12-27 已結束，不因標題含 Live 就加入。中／南部分台不當北部備援。 |

其他排除：`live02.rfi.fr` 的舊 HTTPS Monde 64／96k 入口當次無法連線；公開目錄的中廣舊 `aw9uqyxy2tzuv` 雖 HTTP 200，可解碼，但與現行官方流行網 30 秒同時取樣的指紋不吻合（最佳約 13.497 mismatch bits/frame，157 frames 重疊），未確認同內容，**不加入**。指紋數值只供當次比較，不是數學上的永久同台保證。

## 驗證方法與已知失敗

離線（不連電台）：

```powershell
dotnet run --project tests/Radio.Core.Tests -c Release
dotnet run --project tests/Radio.Windows.Tests -c Release -- --backups
dotnet run --project tests/Radio.Windows.Tests -c Release -- --stability
dotnet run --project tests/Radio.Windows.Tests -c Release -- --system
dotnet run --project tests/Radio.Windows.Tests -c Release -- --diagnostics
```

`--stability` 使用受控時間，驗證全部八台、單一來源、短緩衝／累積停頓、隱藏視窗、來源耗盡、重播重置、計次／音量／靜音、排程後停止／切台及退出釋放；不要求使用者保持真實滑鼠位置。

顯式線上（靜音、不寫使用者設定、不保存音訊）：

```powershell
dotnet run --project tests/Radio.Windows.Tests -c Release -- --kiss-live
dotnet run --project tests/Radio.Windows.Tests -c Release -- --backups-live
dotnet run --project tests/Radio.Windows.Tests -c Release -- --stability-live
dotnet run --project src/Radio.Windows -c Release -- --probe --station esradio --fallback --fallback-index 1 --seconds 30 --output artifacts/esradio-aac-backup.json
```

- 在加入中途換源前的 `--backups-live`：中廣固定入口 30 秒前進 28.0／30.0 秒，沒有連續停頓取樣；RFI 替代入口兩次分別前進 17.6／9.1 秒，最長停頓 10／20 個取樣；esRadio low MP3 前進 14.3 秒、med AAC 18.2 秒，**RFI／esRadio 連續性檢查失敗**。這些來源能開播，不代表當天穩定。
- Windows MediaPlayer 相容後端額外抽查仍未通過：RFI 30 秒前進25.65秒、esRadio low18.23秒／AAC18.43秒，低於既有90%門檻；未因此改預設引擎或放寬原門檻。
- `--backups-live` 是連續性驗證，失敗不得稱為通過；加入監測後可能觀測到下一來源或耗盡失敗。單來源基線可用獨立 `--probe --fallback --fallback-index N`，不冒充自動切換測試。
- `--stability-live` 最多觀測 esRadio／RFI 各120秒，驗證真實故障下依序換源、無循環／重複計次；若最後 Failed，只表示全數用盡，**政策驗證通過不是串流穩定通過**。詳細結果記錄於 PR。
- 加入共用監測後的實際線上政策測試：esRadio 觀測120.1秒，主來源保持Playing，短緩衝未達門檻，因此沒有切備援；RFI 約37.2秒切到替代入口，約99.4秒因持續不穩而全數耗盡、Failed且監測關閉。兩者整輪主來源只解析一次、收聽只計一次；RFI不是穩定播放驗收通過。
- 最終政策（另排除播放時鐘粗粒度回報的假性損失）再次實測：esRadio 約92.2秒自動切至 low MP3，120.2秒觀測結束時仍Buffering；RFI 約22.2秒自動切替代入口，120.3秒時Playing。兩台皆主來源一次、備援索引 `[0]`、整輪計次1。證實真實網路中途切換有發生，**不把 esRadio 結束時緩衝或來源能開播描述成穩定通過**。
- KISS 先前普通／強制主來源失敗的兩次20秒靜音開播驗證成功；仍不是聽感、數小時穩定性、睡眠恢復或所有網路環境驗收。
- 一般本機診斷沿用既有完整網址紀錄規則，見[診斷文件](diagnostics.md)；**Twitch 新來源特別只記 CDN host，遮蔽可能含簽章的 path/query**。probe／console 不輸出短效播放 token。產物與測試截圖不提交。
