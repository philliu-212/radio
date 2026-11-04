# 備援來源掃描與穩定度機制（#94／#96／#97）

2026-11-04 掃描目前全部八台；不把搜尋結果、第三方播放器入口、不同地區分台或節目重播當作已驗證備援。此頁是當次觀測，不保證往後每個端點持續可用。

## 共用機制

- 每次手動播放是一輪：**主來源 → 依目錄順序的備援**。每個來源每輪最多一次，最多三個備援；不回頭重試。停止再播放／手動重新播放會從主來源重新開始，不沿用先前的失敗名單。
- 同一輪另對已解析的完整串流 URL 做記憶體去重：像中廣固定入口若與剛才已開播的 API 結果相同，就跳過，不以「備援」名義重新開同一網址；只有 API 解析失敗而尚未拿到串流時，它才可避開 API 提供幫助。手動重播也清除此去重集合。
- 所有電台都監測，包括只有單一來源的電台。每秒讀一次播放時鐘；連續 **8 秒**沒有至少 0.05 秒的進展，或最近 **30 秒累積損失 12 秒**播放進度，視為持續不穩。短暫緩衝不立即換源；不只依賴引擎的 Playing／Buffering 文字狀態。
- 開播解析／播放失敗、開播逾時、播放中 Failed 或上述持續不穩時，切下一來源。單一來源或全數用盡時停止、顯示失敗並提供手動重試，不在同一台／來源間無限重連。
- 有備援者每個來源開播上限 30 秒；一次開播切換工作總預算為剩餘來源數 × 30 秒 + 10 秒。無備援者保留原本 40 秒開播上限。成功播放後持續監測，不限制收聽時間。
- 停止／切台／退出會取消開播與已排程的換源；自動換源不增加收聽次數、不更換電台識別，保留音量／靜音。手動新一輪真的 Playing 才再次計次。
- 隱藏／最小化仍監測，與可見 UI／metadata 動畫分開；停止／退出釋放 timer。睡眠／UI 長時間未排程（取樣間隔 > 5 秒）及時間軸跳動會重設判斷基準，不直接歸咎來源故障。
- 僅改來源編排與穩定度判斷，沒有換播放器引擎、代理、錄音、預載等待或登入／地區限制繞過。不同 URL 仍可能共用同一機房，全部不穩時不能憑空恢復聲音。

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
