# 六台歌曲資訊可行性調查

調查日期：2026-10-27。這是實際 HTTP／串流抽樣結果，不是六台皆支援的承諾。未找到不等於不存在；節目時段與來源內容會變。

2026-10-28 更新：Direction C 已接上中廣節目／DJ、Hit FM DJ 與可用時的 KISS 預告歌曲，皆為可取消的額外文字查詢。中廣已在實際 portable UI 取得並顯示 `趙少康時間`／`趙少康`；實作與背景查詢規則見 [ui-direction-c.md](ui-direction-c.md)。不宣稱 KISS 的拒絕／穩定性問題已解決。

| 電台 | 已驗證取得的內容 | 歌曲資訊結論 |
| --- | --- | --- |
| 好事989 | 官網有六台中對應的 HLS 音訊入口；首頁及 `javascript/main.js` 未找到即時歌曲查詢 | 尚未找到可靠即時歌名來源 |
| KISS Radio | 官方 JSON 回傳時間、歌手、歌名，語意為「即將播放」 | 可以取得預告歌曲，但未證實與當下音訊同步；重測遇到 403，不能承諾穩定可用 |
| POP Radio | 首頁歌曲條目是排行榜；直播頁 AJAX 取得的是播放網址 | 尚未找到可靠即時歌名來源 |
| 飛碟 | Revma 音訊支援 ICY metadata 協定，但此次三個抽樣 metadata block 均空白 | 尚未驗證有歌名，不能判定永遠沒有 |
| 中廣流行網 | 官方 API 有節目／DJ；ICY `StreamTitle` 此次也是節目名稱 | 可取得節目資訊；尚未驗證歌曲資訊 |
| Hit FM 北部 | 官方 on-air JSON 回傳 DJ 圖片與姓名；歌曲查詢頁是需人工填寫／驗證碼的詢問表單 | 尚未找到可靠即時歌名來源 |

## 可重現的官方來源與證據

### KISS

- 腳本：<https://www.kiss.com.tw/kiss2/js/loadssl.js>
- 腳本使用：<https://www.kiss.com.tw/templates/header_songlist_json.php?area=0>，取 `now_playing`，官網更新間隔 60 秒。
- 2026-10-27 09:35:49 GMT 的 HTTP 200 抽樣：

```json
{"now_playing":"17:35 即將播放：郭家瑋 - 我也我也"}
```

- 後續以相同研究 User-Agent 重測該 endpoint、`/service/songlist/` 和 `/m/songlist.php` 均回 HTTP 403。停止該輪存取，不繞過拒絕；403 的原因未確定。
- 歷史歌單頁首次曾回 200，宣告可查近三個月；尚未驗證可無互動取得當日最近一筆及其準確播放時間，因此不能以歷史頁補足「正在播放」的證據。

### 中廣

- API：<https://www.bcc.com.tw/webapi/BCCRadioWebAPI/ChannelInfoBat>，流行網抽樣節目 `文茜的異想世界`、DJ `陳文茜`，未見歌曲欄位。
- 公開串流：`https://stream.rcs.revma.com/s1zttsg3qtzuv`。
- 傳送 `Icy-MetaData: 1` 後取得 `icy-metaint: 16000`，抽樣內容：

```text
StreamTitle=' - 文茜的異想世界-週二5PM-3(週五4AM重播)';
```

`StreamTitle` 是通用標籤，不保證值就是歌曲。

### 飛碟

- 官網：<https://www.uforadio.com.tw/>；播放器腳本 `/frontend_assets/js/script.js` 未找到歌曲查詢來源。
- 公開串流：`https://stream.rcs.revma.com/em90w4aeewzuv`。
- 同樣取得 `icy-metaint: 16000`，三個抽樣 block 無非空文字。
- 首頁 `.song` 的 YouTube 連結未證實是當下播出內容，不能當成即時歌單。

### 好事、POP、Hit FM

- 好事：<http://www.bestradio.com.tw/>，檢視首頁與 `/javascript/main.js`，未辨認出歌曲 endpoint。
- POP：<https://www.pop917.com/home.aspx>、<https://www.pop917.com/liveStream.aspx?id=1>、`/js/function.js`。歌曲條目屬排行榜；直播頁 `ajax.aspx` 的 `getLIVEURL` 是音訊來源解析，不是歌名。
- Hit FM：<https://www.hitoradio.com/newweb/nowonaircount_new.php?block=0&area=6> 呼叫 `/newweb/onair_n_ajax_cname.php`，抽樣 `value2` 為 `不累DJ - Bibi趙之璧`，`value1` 為 DJ 圖片 HTML。
- <https://www.hitoradio.com/newweb/search.php> 是人工歌曲詢問表單，不是已驗證的即時歌曲 API；未提交表單。

## 調查範圍與實作界線

- 檢查公開官方 HTML、相關腳本及上述 API；HTTP 有 timeout 與讀取上限。ICY 只讀有限音訊位元組以檢查 metadata，沒有儲存／播放／辨識音訊，檢查後關閉。
- 沒有全面解碼 HLS 片段或驗證 timed ID3；不能宣稱 HLS 中絕無歌名。沒有跨音樂時段長時間監測，也沒有用聽音對照驗證 KISS 的時間差。
- ICY 抽樣為開發調查用的短暫獨立連線，**不是**產品應另開音訊串流的設計。現有系統播放器是否能直接暴露相關 metadata 尚未實作驗證。
- UI 目前可規劃「有可用資料才顯示」；KISS 必須保留「即將播放」標示，中廣可另標「節目」。目前六台中沒有一台已完成可靠、與聲音同步的正在播放歌名驗證。
- 若後續實作，僅展開時查詢、縮小時取消額外查詢；切台清除舊值，失效／拒絕／過期時不沿用舊資料。無須為了官方 JSON 引入瀏覽器或大型 UI 框架。
