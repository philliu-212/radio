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

## HLS 開播／卡住恢復

預設 system backend 若 MediaEngine 的 HLS 開播逾時，或播放時鐘看門狗判定卡住，後續來源嘗試／重連會改用既有 Windows 相容引擎及其獨立 HLS HTTP client，不再無限重建同一個失效路徑。同 host 在本程序中保留相容模式，其他 host 與非 HLS 不受影響；不寫入 portable 設定，不自動重啟播放器。來源額度、退避及使用者停止行為維持不變。兩條路徑皆失敗時仍有界退避，不保證上游斷線也能播放。

```powershell
dotnet run --project tests/Radio.Windows.Tests -c Release -- --hls-recovery
dotnet run --project tests/Radio.Windows.Tests -c Release -- --hls-recovery-live
```

前者為離線 fixture；後者刻意使第一個引擎無回應，使用真實 40 秒逾時、15 秒重連計時與好事989官方串流驗證相容引擎恢復。不是原故障根因重現、耳聽或長時間驗收。

## 離線驗證

```powershell
dotnet run --project tests/Radio.Windows.Tests -c Release -- --diagnostics
```

涵蓋容量／輪替／重開、JSON 完整性、去重／限流、完整網址／Referrer／錯誤與堆疊保留、時間戳記、超長欄位截斷、記憶體欄位、I/O 失敗容忍與停止寫入。
不替代 15 小時線上串流或實際斷網驗收。
