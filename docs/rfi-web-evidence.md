# RFI：Web 與 Radio 的證據界線

日期：2026-11-17。追蹤 [#43](https://github.com/philliu-212/radio/issues/43)。這是診斷紀錄，不是已交付修正。

## 已直接量到的差異

本機安裝的 Edge `154.0.4258.37`，由真正 RFI 官網的「Écouter le direct」按鈕開始播放。實際來源確認為：

`https://rfimonde64k.ice.infomaniak.ch/rfimonde-64.mp3`

CDP 媒體日誌直接記錄：

```text
FFmpegDemuxer: created audio stream ... codec: mp3 ... samples_per_second: 22050
kAudioDecoderName = SymphoniaAudioDecoder
kIsPlatformAudioDecoder = false
kRendererName = RendererImpl
```

Radio 的 `MediaEngineAudioPlayer` 則建立 `IMFMediaEngine`，將網址傳給 `SetSource`／`Play`，走 Windows Media Foundation。**不能因為兩者都在 Windows 上，就說它們用同一套 MP3 讀取／解析／解碼路徑。** 這個差異已確認，但沒有因此證明某一個 decoder 有 bug。

## 官網對照：600 秒

原始報告：本機暫存目錄 `opencode/rfi43-real-edge-official-600.json`，UTC 完成時間 `2026-11-17T07:53:30Z`。

| 指標 | Edge 官網 | Radio SystemAudioPlayer |
| --- | ---: | ---: |
| ≥0.5 秒時鐘停滯次數 | 0 | 4 |
| 開播後 waiting／Buffering episode | 0 | 4 |
| 最長連播觀測 | 596.25 秒 | 562.23 秒 |
| 開播後首次中斷前 | 本輪未中斷 | 11.27 秒 |

Radio 的四段停滯約在請求後 `13.58–17.58`、`19.59–23.60`、`25.85–29.61`、`32.37–37.87` 秒，之後本輪連播到結束。因此不能說 Radio 整段十分鐘都一直斷，也不能把開播後等待反覆出現的問題當作已解決。

限制：這輪是 **headless、靜音**、不同上游連線；沒有套用 MainForm 的換源／恢復政策。Edge 取樣最大間隔約 0.252 秒、Radio 約 0.273 秒，未見秒級取樣缺口；但時鐘與狀態事件不是實際聽感或一小時驗收。執行期間有其他診斷連線，不能當作隔離的日常網路基準。仍需同資料對照，不能只靠這張表定位根因。

## 單上游、同位元組對照：300 秒

原始報告：`opencode/rfi43-real-edge-same-data-fixed-300.json`，UTC 完成時間 `2026-11-17T08:00:33Z`。本輪在前述其他線上測試結束後執行。

一條真正的 RFI 上游連線，把每個相同片段立即送給兩個本機讀取端：Edge 的受控 HTML audio，以及未修改的 Radio `SystemAudioPlayer`。這是瀏覽器 media pipeline 的隔離對照，**不是官網 UI 對照**；不能冒稱同一條官網連線。上游 HTTP 200、`audio/mpeg`；分流報告只有一個上游、兩個 consumers，最大本機轉交寫入耗時約 1.87ms。沒有固定預載倒數、回放佇列或重連政策。

| 指標 | Edge HTML audio | Radio SystemAudioPlayer |
| --- | ---: | ---: |
| 首次時鐘前進（含本輪連線等待） | 21.40 秒 | 18.27 秒 |
| ≥0.5 秒時鐘停滯次數 | 0 | 3 |
| 開播後 waiting／Buffering episode | 0 | 3 |
| 最長連播觀測 | 278.75 秒 | 106.72 秒 |
| 暫停／播放錯誤 | 0／0 | 0／0 |

本輪開播慢不能省略：上游在最初收到 1,400 bytes 後，有一次約 11.24 秒的讀取等待。它不是程式刻意加入的 20 秒開播等待。Edge 全部取樣均為 visible，沒有 pause；最大取樣間隔約 0.253 秒，Radio 約 0.286 秒。

### 能直接看到緩衝吸收交付間隙的例子

上游約 `135.18–140.09` 秒沒有新的讀取結果（4.91 秒），因此分流沒有新的片段可送給任一端。在這段期間，約同一時段的取樣是：

| 約請求後 | Edge 播放位置 | Edge HTML buffered 終點 | Radio 播放位置／狀態 |
| --- | ---: | ---: | --- |
| 136.90 秒 | 115.783 秒 | 118.073 秒 | 117.494 秒／Buffering |
| 139.65 秒 | 118.534 秒 | 120.842 秒 | 117.494 秒／Buffering |

Edge 的播放位置與可播放區間終點都繼續前進、ahead 維持約 2.3 秒；Radio 的位置不變。**這顯示瀏覽器能用先前收到、尚未全部反映在 HTML 可播放區間的資料，繼續補給播放；不能把 HTML 那 2.3 秒當作所有資料庫存。** CDP 媒體日誌亦確認本輪仍為 FFmpegDemuxer／SymphoniaAudioDecoder，並非突然改走 Windows decoder。

這輪能排除「剛好是兩條上游連線品質不同」作為兩端差異的解釋；不能排除兩條本機 HTTP client、來源解析／解碼／排程及輸出路徑之間的差異，亦不能單獨判定是哪個元件有 bug。仍是 headless、靜音觀測，不是有聲／一小時穩定度驗收。

## 必須修正的緩衝判讀

本輪 HTML `buffered` ahead 中位數約 2.303 秒、最大約 2.325 秒。**這不是瀏覽器所有緩衝的總和。**

Chromium 公開實作有不同層次：

1. [MultiBufferDataSource](https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/platform/media/multi_buffer_data_source.cc)：網路位元組的預讀／快取，`Read()` 先嘗試從快取讀資料；並不是每次 demuxer 需要下一幀才發出一次網路讀取。
2. [FFmpegDemuxerStream::HasAvailableCapacity](https://github.com/chromium/chromium/blob/main/media/filters/ffmpeg_demuxer.cc)：另有 encoded packet 佇列，公開實作的目標約兩秒。HTML 可播放區間不等於全部已下載位元組。
3. [AudioRendererImpl](https://github.com/chromium/chromium/blob/main/media/renderers/audio_renderer_impl.cc)／[AudioRendererAlgorithm](https://github.com/chromium/chromium/blob/main/media/filters/audio_renderer_algorithm.cc)：decoded audio 佇列不足時，沒有明確 latency hint 的情況下會提高補足門檻，避免不足一點就恢復後又立刻耗盡。公開實作是逐步增加且有上限，不是固定在開播前等 20 秒。

以上原始碼是公開 Chromium 實作的機制證據，不是本輪 Edge binary 每個內部參數的量測，也不代表本輪零中斷就是「自適應補足」造成；本輪 Edge 沒有播放中的 waiting，沒有量到它因此提高門檻。

300 秒同資料對照中，約第 141.90 秒，CDP 累計 media body 1,114,330 bytes，播放位置 120.785 秒，HTML ahead 約 2.304 秒。若以來源標示的 64 kbps 粗估，累計資料與已播位置相差約 18.5 秒，遠不只 HTML ahead；這不是「18.5 秒全部已可播放」的量測，也不是固定預載 18.5 秒。CDP `dataReceived` 是瀏覽器資料通知，可能批次／延遲，不能當成封包抵達時刻；來源的交付間隙以上游連續讀取紀錄為準。

Windows `GetBuffered()` 在本輪回傳空範圍；這只表示沒有可用的範圍資訊，**不能推論 Windows 內部緩衝為零**。

## 還不能下的結論

- 不能只根據 decoder 名稱不同，斷言換 decoder 一定解決。
- 不能只看 `buffered` 約兩秒，就斷言瀏覽器沒有額外位元組快取；先前以此排除額外緩衝的說法不成立。
- 不能把 headless／靜音或受控 HTML audio 的結果，冒稱為使用者平常有視窗、可聽的 Edge 驗收。
- 不能把延長恢復窗口當成這些差異的解釋；本輪沒有調整恢復門檻。

可重跑指令、同位元組分流與工具限制見 [README 的測試與量測](../README.md#測試與量測)。所有原始報告、Edge 專用暫存 profile 均不提交 Git，不保存實際音訊、不修改使用者設定。

## 2026-11-18：本機試聽候選

使用者要求提供獨立試聽版，因而加入明確 opt-in 的 `--rfi-buffered`：背景持續接收、支援逐幀變動碼率、記憶體隊列小於 30 秒，經既有 Windows 解碼／輸出。不啟動瀏覽器或 HTTP gateway，不延長 MainForm 恢復窗口，不加入固定預載倒數；預設與非 RFI 路徑不變。

首個嚴格只接受 64 kbps 幀的候選曾漏掉有效資料；該輪雖顯示中斷減少，**不列為成功驗收，也不交付該版**。後續直播直接確認同一來源混有 64／80 kbps 幀，已修正逐幀碼率解析，並補入不同碼率交錯、假同步 header 與零有效幀丟棄的離線測試。

修正後同上游、同位元組 180 秒對照（`opencode/rfi43-buffered-vbr-same-data-180.json`）：

| 指標 | 預設系統路徑 | 有界背景接收候選 |
| --- | ---: | ---: |
| 首次時鐘前進 | 3.22 秒 | 2.97 秒 |
| ≥0.5 秒時鐘停滯 | 3 次 | 0 次 |
| 開播後 Buffering episode | 3 次 | 0 次 |
| 最長連播觀測 | 103.22 秒 | 177.14 秒 |
| 播放錯誤 | 0 | 0 |

候選接收 7,108 幀：64 kbps 6,534 幀、80 kbps 574 幀；`skippedBytes` 只在初始不完整幀同步時為 61 bytes，後續整輪沒有增加。最大觀測隊列 260 幀（約 6.79 秒），遠低於容量上限；沒有等隊列填滿才開播。

真正 MainForm 另跑 90 秒，約第 30 秒最小化、第 60 秒恢復：時鐘停滯／Buffering／播放錯誤皆 0，連播觀測 86.71 秒，未觸發換源／待重連，初始同步跳過 137 bytes 後沒有再丟資料（`opencode/rfi43-buffered-ui-live-90.json`）。這輪使用原本 source watch／恢復政策，不存設定或截圖。

離線 Core、既有 system／UI、新 buffered source 測試與 Release 組態建置均通過。新增驗證包含隊列滿／等待資料時停止、開播中 Stop 不依賴呼叫者取消 token、重開無晚到事件、可變碼率資料完整性、RFI-only 路由、音量／靜音與本機合成 MP3 的 Windows 解碼。

**上述仍是短時間、靜音的候選驗證。** 本機平面試聽包需另由使用者實際收聽 20–60 分鐘，回報中斷次數、首次中斷前與恢復後的連播時間；不宣稱一小時零中斷、未自行合併或發布正式 Release。
