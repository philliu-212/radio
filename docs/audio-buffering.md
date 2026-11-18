# 共用音訊緩衝

追蹤：[#119](https://github.com/philliu-212/radio/issues/119)，RFI 試聽與直播驗收另見 [#43](https://github.com/philliu-212/radio/issues/43)。

## 機制與套用範圍

`AudioFrameBuffer<T>` 將背景接收、壓縮幀佇列與播放端取資料分開。格式接收器提供完整幀、payload bytes 與播放時長，透過 `ReceiveAsync` 背景接收及 `ReadAsync` 取用；不依賴 RFI 網址或特定編碼。幀入列後不得修改。

`AudioBufferOptions` 設定的是**容量上限，不是開播預載秒數**：

- `Capacity`：最多 60 秒，以各幀實際時長累加。
- `MaxPayloadBytes`：最多 8 MiB，以各幀實際資料長度累加，不依賴來源宣告碼率。
- `MaxFrames`：最多 8,192 幀，限制極小幀造成的物件數量。

三項上限同時生效，最先到達者限制接收；滿時等待，不丟幀、不覆蓋舊資料。停止時必須取消接收與待讀請求、`Complete(discard: true)` 清空佇列，並釋放接收器／網路資源。EOF／錯誤會先讓已收幀排空，再向讀取端回報原始錯誤。這不是錄音、磁碟快取或暫停回放。

接收器可以使用自己的容量設定，例如：

```csharp
var options = new AudioBufferOptions(TimeSpan.FromSeconds(10), 256 * 1024);
var reserve = new AudioFrameBuffer<MyFrame>(options,
    frame => frame.Data.Length, frame => frame.Duration);
// 接收工作：await reserve.ReceiveAsync(reader.ReadAsync, onReceived, stop.Token);
// 播放工作：var frame = await reserve.ReadAsync(stop.Token);
```

`MyFrame`／`reader` 由格式 adapter 提供，不是額外加入的編碼器；adapter 自己的單幀大小、parser 暫存與網路等待也必須有界。`onReceived` 在每次成功入列後呼叫，可用於首次開播就緒或統計，不必等待填滿。

目前僅 RFI adapter 使用；保留原有 1,148 幀、30 秒內、600,404 bytes payload 上限與已驗證格式／來源白名單。其他頻道路由、Windows 解碼／輸出、UI 及重連門檻不變。

日後新增頻道時，需先提供／驗證對應的格式接收器與 Windows sample 描述，再選容量設定；不是將所有網址送進 RFI MP3 parser。AAC／HLS 不在本次支援範圍，也不自動對未驗證頻道啟用。緩衝只能吸收有限交付間隙，不能補救長期頻寬不足。

## 記憶體影響

會增加記憶體用量，但佇列不預先配置整份容量。壓縮資料的粗估為：

`碼率（kbps）× 秒數 ÷ 8 = 十進位 KB`

例如 128 kbps × 10 秒約 160 KB；64 kbps × 30 秒約 240 KB。實際可能為變動碼率，因此仍須 byte 上限。RFI 預設最壞 queue payload 為 600,404 bytes（約 586 KiB），接收端另可能持有一個尚未入列的幀（RFI 最多 523 bytes）。

這**不等於程序總記憶體上限或增加量**：另有幀／佇列物件、同步等待、HTTP/TLS/socket 接收緩衝、parser 暫存及 Windows 解碼／輸出緩衝。停止後會移除佇列參考，但 GC 與 Windows 不保證立即將 working set 還給系統。

`RfiBufferedStream.Describe().reserve` 提供同一時點的 `queuedPayloadBytes`、`queuedSeconds`、`queuedFrames`，以及 `peakPayloadBytes`、`peakFrames` 和設定上限。這些是實際佇列計數，不是程序 working set／private bytes，也不是 Windows 所有內部緩衝總和。

## 驗證

```powershell
dotnet run --project tests/Radio.Windows.Tests -c Release -- --audio-buffer-offline
dotnet run --project tests/Radio.Windows.Tests -c Release -- --audio-buffer-allocations
dotnet run --project tests/Radio.Windows.Tests -c Release -- --rfi-buffered-offline
```

共用測試涵蓋不同幀大小／時長、三種獨立容量限制、順序與背壓、背景接收、取消／停止、EOF／錯誤、容量驗證、2,000 幀收取後的 payload 上限，以及 RFI 自訂容量整合。合成幀僅為離線測試，並不表示支援另一種真實編碼；queue payload 驗證也不能冒稱整體程序額外記憶體量測。

### 逐幀記憶體最佳化

- queue entry 與 MP3 幀描述使用值型別，通知物件只在真的需要等待時建立；無等待者的進出列不再逐幀建立通知物件。
- MP3 parser 使用固定 527 bytes scratch，直接檢查 header，不再建立 header 小陣列、暫存 List 或重複複製 payload。
- 每幀交給 Windows 的 payload 仍獨立配置，避免後續解析覆寫 Windows 尚在使用的資料；不冒險提早歸還 pooled array。
- 停止清空時同時釋放 queue backing storage 的參考。這會減少保留物件，不保證作業系統立即降低 working set。

`--audio-buffer-allocations` 使用 Release 組態、預熱後的同步合成資料，量測目前執行緒的 managed allocation，不含 queue 測試資料／輸入陣列的建立。2026-11-18 相同 fixture 前後結果：

| 測項 | 最佳化前 | 最佳化後 |
| --- | ---: | ---: |
| queue 無等待進出列，10,000 幀 | 216 B／幀 | 0 B／幀 |
| MP3 parser，9,999 幀、每幀 209 bytes | 約 655.34 B／幀 | 約 247.34 B／幀 |

parser 配置量減少約 62%；剩餘包含必要的 payload 及接收逾時計時。這是受控熱路徑的配置量／GC 負擔比較，**不是整個程式常駐記憶體減少 62%**，也不是網路等待時完全零配置。容量上限、音訊資料及開播策略不變。

### 長播總記憶體

bounded reserve 不代表整個播放器的記憶體一定有界；parser allocation 測試也未涵蓋 Windows sample 的 COM／GC 生命週期。[RFI 長播記憶體調查](rfi-memory.md) 記錄 #129 的使用者 log、分離診斷命令與改善實驗。sample 改用原生自有 payload copy，避免把 parser 的小陣列透過 `AsBuffer()` 固定到 sample／projection 釋放為止；不縮減 reserve、不強制 GC。
