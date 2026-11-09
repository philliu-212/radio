# 播放診斷紀錄

一般啟動播放器時自動啟用，無須設定；紀錄本身不改變播放／重連策略或 UI。
此功能用於調查長時間播放失敗及記憶體變化，不代表已確認或修復 memory leak。

## 位置及容量

- portable：與 `radio.settings.json` 同一層的 `logs` 目錄（不在版本資源目錄內，因此更新後保留）。
- 開發版：程式目錄的 `logs`；probe 與一般離線 UI 測試不會自動建立紀錄。
- UTF-8 JSON Lines：`playback.log` 為最新，`.1`、`.2`、`.3` 依序較舊。
- 每檔最多 **256 KiB**，含目前檔最多 **4 檔**，內容合計最多 **1 MiB**；重開不清空，滿額自動輪替。檔案系統配置空間不包含在此數字內。
- 不會上傳；關閉播放器後可刪除 `logs`。目錄唯讀、磁碟滿或寫入被鎖定時略過紀錄，不阻止播放。

## 記錄與防洗版

- 每筆含 UTC 時間戳記（`time`，台灣時間加 8 小時）與工作階段識別碼，可區分重開前後。
- 啟動／結束、版本、原始電台 ID、播放請求、播放狀態與錯誤文字、原生數字錯誤碼、例外型別／HRESULT／訊息／內層例外／stack trace。
- 來源解析成功、交給音訊引擎前記錄 `stream-resolved`：完整串流網址、Referrer、媒體類型及是否 HLS。保留 token、簽章、有效期限與其他 query 參數及其原始編碼，便於調查失效來源。只記應用程式解析出的入口，不攔截引擎內部每個 HLS segment 或重新導向請求。
  - 新增的 Twitch 備援是例外：來源 Referrer 為 `www.twitch.tv` 時，僅留下 CDN authority 與 `/[redacted-twitch-stream]`，遮蔽可能含短效授權的 path／query；公開頻道 Referrer 仍保留。其他既有來源的完整診斷行為不變。
- 不另外輪詢或記錄歌曲／DJ metadata。不收集請求 headers、cookies 或回應內容；錯誤文字若含網址等資訊會照實保留。僅存本機、不會上傳，分享前請檢閱；有效期間內的播放網址可能被拿來播放。
- 每個文字欄位超過 8192 個 UTF-16 code units 時截斷並附 `[truncated]`，避免異常巨大的網址或堆疊擠爆單檔；正常長度的網址與錯誤完整保留。
- 相鄰相同電台／狀態／訊息不重複記錄；每個工作階段每分鐘最多 30 筆，超量略過，在下一筆成功紀錄的 `suppressed` 欄位累計。極端事件風暴可能略過部分錯誤。
- 啟動、結束、失敗時與每 **5 分鐘**記錄記憶體快照，不記逐幀／播放進度；不呼叫強制 GC。
- HLS 開播逾時或播放時鐘卡住時，在停止引擎前額外記錄 `player-snapshot`：原因、引擎、generation、最近關鍵原生事件、network／ready state、播放位置與等待狀態；僅故障時取樣，新增欄位不含完整網址／token。診斷失敗不阻止恢復。
- `workingSetBytes` 是工作集，`privateBytes` 是私有記憶體，`managedBytes` 是當下 .NET managed heap 估計；另含 handle、thread 與 GC 次數。這些數字定義不同，不應直接與工作管理員任何單一欄位混用。

## 發生問題時

1. 記下電台名稱、失敗時間、版本，以及當時網路／官網是否正常。
2. 若可行，先保留重開前的 `logs`；重開後仍可收集，但舊資料可能因輪替被覆蓋。
3. 提供四個檔案中實際存在的紀錄及上述資訊。畫面仍顯示簡短錯誤，詳細原因查看 `playback` 的 `message`／`codes`、`exception` 的 `details`，來源查看 `stream-resolved`。

判斷洩漏需看同條件長期趨勢、反覆切台／失敗後是否累積，必要時另採集記憶體 dump；重開後恢復不能單獨證明洩漏。

## HLS 手動相容模式重試

目前不因逾時／卡住自動切換引擎；既有自動重連與退避仍保留，但沿用該電台目前選用的引擎。Hit FM 原有預設相容路徑不變。

下次發生問題時，先不要關閉播放器：

1. 在系統匣圖示右鍵選單，「顯示節目／歌曲資訊」下方點選 **使用相容模式重試**。這是單次動作，不是勾選開關。
2. 程式先記錄舊引擎狀態，取消原開播／重連，再重新解析目前播放目標並使用既有 Windows 相容引擎（獨立 HLS HTTP client）。開播中與等待重連時都可操作；無播放目標、尚未解析出 HLS、非 system backend 或正在量測同次重試時停用。
3. 觀察是否真的恢復聲音，保留 `data/logs/playback.log*`。新診斷 `manual-compatibility-retry` 記錄電台與階段：`requested`、`playing-event`、`clock-progress`（時鐘前進至少 5 秒）、`no-clock-progress`（Playing 後 30 秒仍未達標）、故障原因或 `canceled`。既有卡住看門狗也可能提早判定失敗；`player-snapshot` 保留切換前、開播後及量測結果的引擎狀態。新事件不含串流網址／token，但原有 `stream-resolved` 仍依上文保留完整來源，分享日誌前須注意敏感參數。

相容偏好只記在該電台、本次程序；暫停重播或切台返回仍沿用，其他電台即使同 host 也不受影響，非 HLS 亦不受影響。不寫入 portable 設定、不自動重啟、不啟動外部播放器。重試已成功播放的電台不重複增加播放計數。兩條路徑皆失敗時仍有界退避，不能保證上游斷線時可播放。

**Playing 事件與播放時鐘前進都不是耳聽證據。** 真正有用的對照是同一個實際故障程序中，保留預設引擎失敗與手動切換後的結果。目前尚未證明原始 Windows 內部故障的觸發原因或相容引擎能修復該故障。

```powershell
dotnet run --project tests/Radio.Windows.Tests -c Release -- --hls-recovery
dotnet run --project tests/Radio.Windows.Tests -c Release -- --hls-recovery-live
```

前者為離線 fixture，檢查選單、沒有自動換引擎、重連退避、手動取消開播、電台隔離、計數與診斷結果；後者刻意使第一個引擎無回應，等待真實 40 秒逾時，再觸發相同選單動作，使用好事989官方串流驗證播放時鐘、暫停重播及 POP 切台返回。不是原故障根因重現、原故障程序 A/B 對照、耳聽或長時間驗收。

## 離線驗證

```powershell
dotnet run --project tests/Radio.Windows.Tests -c Release -- --diagnostics
```

涵蓋容量／輪替／重開、JSON 完整性、去重／限流、完整網址／Referrer／錯誤與堆疊保留、時間戳記、超長欄位截斷、記憶體欄位、I/O 失敗容忍與停止寫入。
不替代 15 小時線上串流或實際斷網驗收。
