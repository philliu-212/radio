# RFI 切回 BEST 98.9 記憶體調查

追蹤：[#138](https://github.com/philliu-212/radio/issues/138)，延續 [#129](https://github.com/philliu-212/radio/issues/129)。

## 問題與驗收缺口

使用者 v0.5.9 的 session `a41a9d66` 在 2026-11-19 11:26:57（UTC+8）從 RFI 切回 BEST。11:48:28 private 為 64.41 MiB、managed 3.38 MiB，Gen2 次數從切台前 3 增至 5；11:58:28 private 仍為 66.23 MiB、managed 2.79 MiB。不能以 managed 已下降就認定整個程序的空間已歸還。

只播 BEST 的 33.6 MiB 歷史參考來自另一個 v0.5.8 程序，不是回收的精確目標，也不是同版本 A/B。#129 的長播測試沒有驗收切回 BEST 後的記憶體或反覆切台，不能沿用其結論宣稱切台沒有問題。

## 同版本診斷

```powershell
dotnet build tests/Radio.Windows.Tests -c Release
dotnet run --project tests/Radio.Windows.Tests -c Release --no-build --no-restore -- --rfi-memory --mode switch-live --seconds 60 --output artifacts/rfi-switch-live.jsonl
dotnet run --project tests/Radio.Windows.Tests -c Release --no-build --no-restore -- --rfi-memory --mode best-only --seconds 60 --output artifacts/rfi-switch-baseline.jsonl
```

兩個命令使用獨立程序，**依序**執行，不操作 portable、不保存設定，全程靜音：

- `best-only`：只播 BEST，觀測時間為 `seconds × (2 × cycles + 6)`。
- `switch-live`：BEST 基準一段，再 `cycles` 輪 RFI → BEST，各一段，最後 BEST 再等待 `seconds × 5`；總觀測時間與只播模式相同，另加解析／開啟時間。
- `--cycles` 預設 3、範圍 1–50；切台診斷的 `--seconds` 範圍 10–600。短段多輪是生命週期壓力測試，不取代單台長播或切回後等待一小時。
- `switch-synthetic`：相同系統路由，RFI 的網路輸入替換為無限靜音 MP3，BEST 仍是線上來源；不是全離線測試或真實 RFI 接收驗收。
- `switch-ui-live`／`best-ui-only`：相同切台／只播對照，但透過真正 MainForm 開啟電台，保留 metadata 與既有 source-watch／recovery policy；額外記錄目前電台、狀態、reconnect pending 與監控是否運行。不注入故障，不能僅因使用 MainForm 就聲稱曾完成自動重連。
- `--recovery-seconds` 可獨立指定切回 BEST 後的等待時間（10–7200 秒），預設仍為 `seconds × 5`。兩種只播模式總觀察時間改為 `seconds × (2 × cycles + 1) + recovery-seconds`，因此對照仍等長。UI 模式使用獨立設定物件、不保存偏好、不檢查更新；會顯示測試視窗與自己的 tray icon，不操作既有 portable 視窗。
- 每十秒輸出 private／working set／managed／GC committed、fragmentation、GC 次數、handles、threads、播放時鐘與 retired graph 的弱參考狀態。開啟失敗、Failed、觀察期間時鐘未前進或 receiver 未結束都會失敗。
- 切台前只保留 RFI adapter、MediaPlayer／MediaSource 投影、MediaStreamSource、buffer、HTTP client 與取消來源的弱參考；切台後等 receiving task 結束並清掉強參考，再量測自然回收。
- 所有自然取樣結束後，才在 BEST 仍播放時做一次診斷 GC／finalizer／GC。這不是產品修正，也不把 GC 後的低值冒充自然穩態。

弱參考死亡只能證明相對應 managed graph 不再存活，不能證明原生配置器已歸還所有 committed pages。GC committed 是最近一次 GC 的統計，不能將 `private - committed` 精確等同所有 native allocations。

## 程式碼檢查範圍

- `SystemAudioPlayer.Stop` 先移除事件轉送，再 Dispose 原 adapter。
- `WindowsAudioPlayer.Stop` 取消 RFI 開啟／接收、移除事件、清空 player source，Dispose MediaPlayer／MediaSource，最後 Dispose RFI bridge。
- `RfiMediaStreamSource.Dispose` 取消 outstanding request 並移除 Starting／SampleRequested／Failed handlers。
- `RfiBufferedStream.Dispose` 取消並 Dispose HTTP、丟棄 queue；receiver 的 finally 關閉輸入與 response，完成後 Dispose CTS。

不能只靠看到 Dispose 呼叫就宣告沒有洩漏；上述流程需要配合 receiver 完成、弱參考與反覆切台實測。

## 結果

### 離線生命週期回歸

2026-11-19 使用同份來源執行 Fast＋Audio，兩專案建置與 **12/12 群組通過**。報告：本機 `artifacts/test-results/20261119-120409-a28ab6b9a2ee4e58ba542ddb15939cfb/summary.json`。

新增回歸在真正 Windows 音訊管線播放合成 RFI，透過 SystemAudioPlayer 切到另一 adapter，重複三次。每次舊 receiver 必須兩秒內結束；最後等 STA callbacks 清空並做診斷 GC，驗證三輪 RFI adapter、MediaPlayer／MediaSource／MediaStreamSource 投影、buffer、CTS 全部不再存活。合成來源沒有 HTTP，因此 HTTP 生命週期仍須線上結果佐證。這不是「自然回收後 private 一定回到初始值」的斷言。

第一次 `./scripts/Test.ps1` 被本機執行政策拒絕，沒有執行測試；改用文件允許的**程序範圍** `powershell -NoProfile -ExecutionPolicy Bypass -Command "& ./scripts/Test.ps1 -Profile Fast,Audio"`，未更改全機政策。

### 線上量測

第一輪因工具設為 120 秒上限被中止，只完成 BEST 基準與部分 RFI 第一段，**不算通過**（`artifacts/rfi-switch-live-138.jsonl`）。完整切台與只播 BEST 對照改用 1,800 秒工具上限依序執行，播放中不做 GC 或 dump。

同版本 0.5.9，獨立靜音程序依序完成：

- `artifacts/rfi-switch-live-138-complete.jsonl`：三輪，每台每段 60 秒，最後 BEST 另等 300 秒，總約 732 秒，零 Failed。
- `artifacts/rfi-switch-baseline-138.jsonl`：只播 BEST 720 秒，總約 726 秒，零 Failed。

| 同版本、BEST 仍播放時 | 只播 BEST private | 三輪 RFI 後 private | 只播 BEST managed | 三輪 RFI 後 managed | 只播 BEST GC committed | 三輪 RFI 後 GC committed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 自然量測結尾 | 27.57 | 52.14 | 2.03 | 12.46 | 2.24 | 13.88 |
| 結尾診斷 GC 後 | 28.02 | 49.62 | 0.48 | 1.20 | 2.61 | 13.56 |

單位 MiB。三次切回 BEST 後各 60 秒的 private 為 44.80、48.69、51.36；最後繼續播 BEST 的五分鐘 private 為 51.40–52.70。

三個舊 RFI receivers 都已完成。自然結尾前兩輪的全部弱參考已死亡，第三輪仍存活；結尾診斷 GC 後三輪 **24/24 弱參考全部死亡**，包括各 HTTP client。這證明第三輪當下包含延後 GC，但 GC 後 private 仍高 **21.61 MiB**，不能把整個差額歸咎於「沒等 GC」。

GC 後的 GC committed 差 **10.95 MiB**，而 managed 存活量只差約 0.71 MiB，顯示 GC 已回收物件卻仍保留明顯較大的 committed heap。其餘 private 差額不能用簡單相減精確歸類為 Windows decoder，仍包含載入／JIT／原生配置器等空間。未做 native heap allocation stack 分析，不能宣稱已定位所有原生配置或排除原生洩漏。

針對三輪 private 仍增加的疑點，另做 12 輪短段切台壓力對照，新增 VirtualQuery committed 類別與 module 名稱取樣；它們只協助辨識載入／平台成本，不把映像映射大小當成實際私有用量。

最新同份來源（含 cycles 參數、VirtualQuery 及 modules 診斷）再次 Fast＋Audio 建置與 **12/12 群組通過**，報告 `artifacts/test-results/20261119-123047-ec145ac0902143b0bd353116551a7e3a/summary.json`。未變更產品播放程式碼，也未加入強制 GC。

### 12 輪切台壓力對照

相同來源／Release，`--seconds 10 --cycles 12`，獨立程序依序執行：

- `artifacts/rfi-switch-stress-138.jsonl`：12 輪真實 RFI → BEST，加最後 50 秒 BEST，總約 343 秒。
- `artifacts/rfi-switch-stress-baseline-138.jsonl`：只播 BEST 300 秒，總約 306 秒。
- 兩個程序均零 Failed，全部觀察段落有播放時鐘前進；不是聽覺無卡頓或一小時驗收。

| 同版本、BEST 仍播放時 | 只播 BEST private | 12 輪 RFI 後 private | 只播 BEST GC committed | 12 輪 RFI 後 GC committed |
| --- | ---: | ---: | ---: | ---: |
| 自然結尾 | 27.86 | 51.54 | 3.11 | 16.76 |
| 診斷 GC 後 | 28.52 | 49.87 | 3.17 | 15.00 |

單位 MiB。每輪切回 BEST 後 private：34.06、39.71、43.79、45.34、47.24、47.64、48.72、49.71、49.00、50.28、50.27、50.66。初期上升後趨於較高平台；最後三輪沒有按前幾輪的幅度一直累加。12 輪後診斷 GC 的 49.87 MiB 也接近三輪的 49.62 MiB，但這是不同程序／網路資料，不是無限次切台皆不洩漏的保證。

12 個舊 receiver 全部結束。**尚未執行診斷 GC 的自然結尾，96/96 弱參考已全部死亡**。私有記憶體沒有隨這些 graph 的死亡回到 BEST-only 基準。

播過 RFI 後的 module 清單新增 `WinRT.Runtime.dll`、`Microsoft.Windows.SDK.NET.dll`、`Windows.Media.Playback.MediaPlayer.dll`、`CryptoWinRT.dll`、`mp3dmod.dll`、`resampledmo.dll` 等，切回 BEST 後仍載入。BEST 預設使用 MediaEngine，而 RFI 使用 WinRT MediaPlayer／自管壓縮幀來源，這是兩套播放路徑共存的實測證據。映像 committed 約 166.6 → 200.6 MiB **不是增加了 34 MiB 私有記憶體**，不能與 private 或 working set 混算。module 列表與 JSON 序列化也會引入診斷成本；因此兩個對照程序都使用相同取樣器，且不拿這輪與前一輪當嚴格效能 A/B。

### 可以下的結論與尚未定位的部分

1. **沒有等待多久就必定回到 33.6 MiB 的承諾。** 同版本實測自然回收及診斷 GC 都沒有回到只播 BEST 的 private 基準。
2. 已確認差額包含較大的 **GC committed heap 保留**；播過 RFI 還會讓另一套播放元件留在程序裡。關閉舊播放器不等於卸載整個 WinRT／decoder 基礎設施或歸還所有空白 heap pages。
3. 追蹤到的舊 RFI managed graph／HTTP client 沒有永久殘留，receiver 均結束。12 輪內用量趨於平台，**沒有觀察到按輪數持續等幅上升的證據**，但不等於排除所有 native leaks。
4. **仍未完成：原生／runtime 差額的 allocation stack 歸屬。** 沒有 native heap trace，不能聲稱每一 MiB 都是合理快取、已完全排除原生洩漏，或把使用者包含 MainForm recovery 的較高數字完全歸因於這份隔離實驗。故 #138 仍為 open，本次 PR 保持 draft，不把調查工具當成產品修復交付。
5. 沒有加入定時／切台強制 GC、工作集裁切或重啟播放器來美化數字；沒有修改 portable。後續產品改善應依原生配置證據決定，不以改動串流路由或犧牲既有緩衝行為作為未驗證的修正。

### 最終離線回歸

因新增 CLI 的 `--cycles` 解析，補跑完整 `-Profile Offline`：兩專案建置與 **22/22 群組通過**，報告 `artifacts/test-results/20261119-124404-8bd74488931641a185f1fe4b329b6401/summary.json`。後續只補本文件結果，未修改原始碼。未做 native heap tracing、一小時切回等待、MainForm 線上 recovery、多小時／可聽音質、實體多螢幕或 Release。

## 後續：實際 MainForm 切台對照

2026-11-19 接續 #138，補上 `switch-ui-live`／`best-ui-only` 與可獨立指定的恢復等待時間。產品播放程式碼仍未修改，沒有加入手動 GC 或工作集裁切。

```powershell
dotnet run --project tests/Radio.Windows.Tests -c Release --no-build --no-restore -- --rfi-memory --mode switch-ui-live --seconds 10 --cycles 3 --recovery-seconds 60 --output artifacts/rfi-switch-ui-138-short.jsonl
dotnet run --project tests/Radio.Windows.Tests -c Release --no-build --no-restore -- --rfi-memory --mode best-ui-only --seconds 10 --cycles 3 --recovery-seconds 60 --output artifacts/rfi-switch-ui-baseline-138-short.jsonl
```

兩個獨立程序依序執行；兩者觀察時間都是 130 秒，另加開台、診斷 GC 與停止時間，總長分別約 146／137 秒。兩者零 Failed，各觀察段時鐘有前進；這不是每個取樣區間無停頓或聽覺驗收。

| MainForm，BEST 仍播放時 | 只播 BEST private | 三輪 RFI 後 private | 只播 BEST managed | 三輪 RFI 後 managed | 只播 BEST 最近 GC committed | 三輪 RFI 後最近 GC committed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 自然結尾 | 32.88 | 46.87 | 4.84 | 4.21 | 0.00 | 14.85 |
| 結尾診斷 GC 後 | 34.48 | 40.17 | 1.64 | 2.22 | 7.67 | 8.11 |

單位 MiB。只播程序自然結尾的 GC committed 為零，表示尚無 GC 統計，**不是沒有 committed heap**；不可把該列相減作為精確 heap 成本。結尾診斷 GC 後的 committed 差約 0.44 MiB，private 差約 5.69 MiB；和先前無 MainForm 的隔離程序差額不同，不能將其當作產品改善 A/B，或把先前約 21 MiB 差額視為跨情境固定成本。

三個 retired receivers 均結束；自然結尾 24 個弱參考中僅最後一個 adapter 仍存活，診斷 GC 後 **24/24 全部死亡**。自然結尾與 GC 後的 UI 均為 BEST／Playing，metadata 與 playback watch 運行，reconnect pending 為 false。本次沒有故障注入，也沒有證明自動重連分支曾執行。

新增來源已執行 `Fast,Audio`：兩專案建置與 **12/12 群組通過**，報告 `artifacts/test-results/20261119-130434-1c53591fbf7f4b0a8af9f12e8a6625be/summary.json`。含新增 CLI 參數契約；未在這一輪重跑完整 Offline。

### 一小時切回等待：執行中，尚非通過

```powershell
dotnet run --project tests/Radio.Windows.Tests -c Release --no-build --no-restore -- --rfi-memory --mode switch-ui-live --seconds 10 --cycles 3 --recovery-seconds 3600 --output artifacts/rfi-switch-ui-138-hour.jsonl
```

此程序完成三輪切台後繼續播 BEST 一小時，自然觀察期間不做 dump、手動 GC 或工作集裁切；結束自然取樣後才執行既有診斷 GC。結果完成前不列為通過，也不拿上述 130 秒只播對照冒充等長一小時對照。原生 allocation stack 仍未取得；目前沒有足以支持產品修正或關閉 #138 的證據。PR 維持 draft。
