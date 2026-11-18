# RFI 長播記憶體調查

追蹤：[#129](https://github.com/philliu-212/radio/issues/129)。

## 使用者紀錄

2026-11-18，portable 目錄名稱雖含 v0.5.0，log 的實際執行版本為 **0.5.8**。以下為同一個程序／session 的私有記憶體，不與工作集或工作管理員其他欄位混用；單位 MiB，時間 UTC+8。

| 時間 | 私有記憶體 | managed |
| --- | ---: | ---: |
| 17:14:58 | 57.53 | 7.74 |
| 17:19:58 | 79.18 | 7.90 |
| 17:24:58 | 68.47 | 11.80 |
| 17:29:58 | 92.04 | 19.81 |
| 17:34:58 | 105.80 | 17.41 |
| 17:39:58 | 115.23 | 19.63 |
| 17:44:58 | 114.76 | 22.40 |
| 17:49:58 | 125.41 | 25.18 |
| 17:54:58 | 111.53 | 5.29 |
| 18:24:58 | 146.81 | 18.62 |

RFI 約 17:10:57 開始；前一台 BEST 使用不同來源／播放路徑，約 25 分鐘私有記憶體維持 32–34 MiB，只能作參考，不能當成同條件 A/B。RFI handles 大致穩定、沒有反覆重建連線的紀錄；有少數短暫 buffering。17:54 的 managed 曾自行下降，不能將整段增長直接斷言為永久物件洩漏，也不能據此認定原生記憶體已穩定。

使用者後續回報突破 140 MB；18:24 的 log private 已為 146.81 MiB，隨後程序讀值約 159.6 MiB（working set 247.2 MiB）。中途即使曾回落到約 100 MiB，後續仍創新高，不能以「會自行回收」當作沒有問題的保證。這些仍是未更新的 portable，不是修正後程序。

四份輪替 log 中另有 v0.5.7 的一段 RFI（session `77d2314d`，11/17 15:07 開始，到下次切台前最後一筆為 16:17）：14 筆 memory samples，私有記憶體約 27.6 → 26.7 MiB、峰值 36.8 MiB。這是有價值的歷史參考；v0.5.7 的 `SystemAudioPlayer` 尚未套用目前的 RFI buffered routing，版本／管線／播放條件不同，不能將這些數字直接當作修正可達的保證。

## 分離實驗

新增明確 opt-in 的診斷命令，不放入 Fast／Audio 自動長測：

```powershell
dotnet run --project tests/Radio.Windows.Tests -c Release -- --rfi-memory --mode synthetic --seconds 300 --output artifacts/rfi-memory-synthetic.jsonl
```

模式：

- `synthetic`：無限合成靜音 MP3 → 原有 bounded reserve → Windows decoder/output。全程靜音、不接觸電台、不建立 MainForm。
- `queue`：相同合成來源／reserve，約每秒消耗 38 幀，不建立 Windows samples。
- `live`：真實 RFI → 相同 reserve／Windows 播放管線，靜音。使用獨立連線，不中斷 portable。
- `direct`：相同 RFI URL 直接交給 WindowsAudioPlayer 的 URL source，繞過自管 reserve／samples；不是正式路由變更。
- `managed-samples`／`native-samples`：不解碼，約每秒建立 2,000 個小 sample，分別使用舊 AsBuffer 與候選 native copy，只用於放大逐 sample 配置／回收差異，不代表實際播放速度。

每 10 秒串流寫 JSONL，包含 private bytes、working set、managed、GC heap committed／fragmentation／pin 數（後三者是最近一次 GC 的統計）、handles、threads、播放時鐘、sample 交付量與 queue 計數。不將歷史 sample 全部留在記憶體；輸出在 artifacts，不提交個人 logs 或 dumps。

結尾停止後等待 2 秒取樣，再 **僅在診斷程序**做 GC／finalizer／GC，比較延後回收與仍存活物件。這會擾動量測，不能用最後的數字冒充正常播放時記憶體，也不在產品加入週期性 GC。

## 初始定位

舊版合成靜音管線在 5 分鐘內即可重現私有記憶體上升（10 秒約 24.12 MiB、300 秒約 63.15 MiB）。queue 全程 1,148 幀、239,932 bytes、約 29.99 秒，排除了「queue 無限堆資料」與「必須網路卡頓才發生」。這輪在約 40／80 秒擷取 heap dumps，並在約 60 秒由 gcdump 觸發 GC，造成可見的時鐘暫停；不能當成無干擾的連播／精確效能基準。

Heap snapshot 看見 `WindowsRuntimeBuffer` 包住每幀 managed byte array，帶 `_pinHandle`／`_dataPtr`；樣本含數百個 pinned handles 與大量 WinRT finalizable wrappers。`AsBuffer()` 是包裝原陣列，不是把 payload 複製成 Windows-owned storage。這條路徑有小陣列跨原生生命週期固定、晉代／碎片化及延後回收的成本；snapshot 本身尚不足以斷言 Windows 內部永久洩漏。

改善是透過 `CryptographicBuffer.CreateFromByteArray` 建立原生自有 buffer，再交給同一個 `MediaStreamSample.CreateFromBuffer`。不變更 MP3 bytes、時間戳、播放策略、容量與首次幀即可開播的行為；代價是每幀一次小 payload copy。回歸驗證來源陣列被改寫後 sample bytes 仍完整、保留 sample 時來源陣列可被回收，以及既有原生解碼／停止／重播。

原生 copy 的第一輪 300 秒合成播放（未擷取 dump、播放中未強制 GC），60 秒後 private 約 37–42 MiB 波動，300 秒為 40.18 MiB；播放時鐘前進 299.29 秒，交付 11,554 幀，queue 同樣固定在 239,932 bytes。自然 GC 最近的 pinned objects 為 1–2（是 GC 觀測值，不是隨時更新的 pin 總數），GC committed 約 13.3 MiB。停止後診斷 GC 將 managed 降至約 0.90 MiB，這筆只作生命週期參考。

原生 copy 也增加了 buffer projection／copy 的配置成本，不宣稱逐幀 managed allocation 或所有情境的 CPU 都下降。下列另補無 dump 的舊版基準，不將受擾動的第一輪當比較分母。

## 無 dump 對照結果（2026-11-18）

同機 Release、獨立程序、依序執行，播放中沒有手動 GC／heap dump。舊版使用本次修改前保存的 runner／產品 DLL，合成來源、30 秒 reserve、Windows decoder/output 與靜音條件相同；各程序結尾才執行上述診斷 GC。表格皆取**停止前**，不是 GC 後的最低數字。

### 相同 300 秒合成播放

| 指標 | 舊 AsBuffer | 原生自有 copy |
| --- | ---: | ---: |
| 10 秒 private MiB | 24.06 | 24.15 |
| 60 秒附近 private MiB | 35.93 | 37.95 |
| 120 秒附近 private MiB | 45.39 | 40.65 |
| 180 秒附近 private MiB | 53.61 | 41.31 |
| 240 秒附近 private MiB | 61.57 | 40.60 |
| 300 秒 private MiB | 68.91 | 40.18 |
| 300 秒 working set MiB | 134.40 | 105.75 |
| 最後一次 GC committed MiB | 22.83 | 13.28 |
| 實際播放時鐘秒 | 299.29 | 299.29 |
| 交付幀數 | 11,560 | 11,554 |
| queue payload bytes | 239,932 | 239,932 |

舊版每分鐘持續上升；native copy 暖機後在 37.18–42.10 MiB 間波動。這是單機單輪量測，不是全情境的節省比例保證。queue-only 另跑 60 秒，private 約 13.78 → 14.59 MiB；時間短、未觸發 GC，僅作沒有 Windows samples 時的參考，不冒稱完整 queue 長播驗收。

### 逐 sample 放大對照

兩個程序各在 30 秒內建立 **58,200 個 209-byte samples**，不解碼；以相同數量比較，而非與真實播放秒數混用。

| 最後播放／建立中取樣 | managed AsBuffer | native copy |
| --- | ---: | ---: |
| private MiB | 124.43 | 36.47 |
| 最近 GC committed MiB | 34.00 | 14.86 |
| 最近 GC fragmented MiB | 15.28 | 1.04 |
| 最近 GC pinned objects | 14,773 | 2 |

這與 heap snapshot／來源陣列生命週期回歸一致，支持**逐幀 managed buffer 的固定、碎片化及延後釋放，是此次增長的主要可修正成因**。不等於已證明 Windows decoder 本身永久洩漏，也不宣稱所有 native allocations 都已定位。

### 真實 RFI

獨立靜音連線跑 600 秒，沒有 MainForm recovery、沒有手動 GC／dump；不是使用者原本的 portable 程序。

- 實際時鐘前進 597.56 秒，交付 22,969 幀；只出現開播時的 Buffering → Playing，沒有 Failed 或後續 Buffering 事件。
- 60 秒後 private 38.20–47.71 MiB；最後 46.55 MiB。最後五分鐘各分鐘取樣約 44.57、47.05、46.04、46.09、46.55 MiB，沒有舊版合成管線那種持續逐分鐘攀升。
- 結尾 working set **119.03 MiB**，因此不保證工作管理員所有記憶體欄位低於 100 MiB。
- queue 最後 237 幀／49,508 bytes，並未填滿 30 秒 reserve。這是實際有限預取，不把容量上限當成固定預載。
- 不是同一批線上資料的舊／新 A/B，也不是聽覺、弱網注入或數小時連播驗收；不能保證所有網路與長時間情境完全不增長。

## 回歸與交付限制

`./scripts/Test.ps1 -Profile Fast,Audio`：兩個專案建置及 **12/12 測試群組通過**。摘要位於本機 `artifacts/test-results/20261118-182436-17d12f8f9f934bad97631408462f5b86/summary.json`，不提交產物。

新增的 sample ownership 三項斷言、原生合成解碼、停止／重播及既有 HLS／備援／本機 WAV 回歸均通過。未跑完整 Offline／UI／Updates，因未更動相關 UI、OTA 或共用編排；CLI 僅新增 opt-in 診斷入口及契約案例。未執行多小時／實體弱網、可聽音質或多螢幕驗收。

使用者 portable／設定／播放未修改。本次不打包或發布 Release；合併與發行仍需另行確認。

使用者後續回報舊版突破 140 MB 後，另啟動 **3,600 秒真實 RFI 靜音長測**（`artifacts/rfi-memory-live-native-1h.jsonl`），使用同一份已回歸的修正後 binary。此項尚在執行，未計入上述通過項目；PR 先保留 draft，完成後再補實際結果，不預先宣稱一小時穩定。
