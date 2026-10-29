# 電台管理擴充（以 v0.3.6 為基礎）

## UI 與來源

- 沿用原生 WinForms、StationListBox、PlayerButton 與原有深／淺色配色。不更換音訊後端或 metadata provider。
- 主視窗 554 × 271 DIP：僅增加搜尋列所需的 38 DIP，維持欄寬比例、字級、31 DIP 可見電台列（37 DIP 含間隔），以及原有標題列／控制功能。
- 主畫面搜尋列為 30 DIP pill，放大鏡在內，外側 + 間距 12 DIP。搜尋只重建顯示資料，不觸發選台播放。
- 首次與任何 0 台啟動皆先顯示中央 +，不自動開啟管理視窗；點擊 + 才進入管理。
- 管理視窗空白區域與分類圖示可拖曳移動，不增加文字標題列；搜尋、清單、捲動與按鈕不觸發拖曳。視窗採深色 12 DIP 圓角，不繪製外框線。
- 四周 14 DIP 由視窗本身繪製，子元件完整避開圓角，不再使用多層圓角裁切。主畫面及管理清單選取僅顯示底色，不繪製虛線焦點框；鍵盤選台照常運作。
- 管理視窗為 690 × 293 DIP owned modal，主畫面留在背景。無文字標題／數量／手動排序；分類向量圖示獨立於 32 DIP pill，間距 9 DIP。搜尋列右側預留與電台列相同的捲動條空間，pill 右緣與電台列右緣對齊。中間四顆 30 × 30 DIP 圓角框線箭頭：上方 ›／‹ 批次加入／移除選取電台，下方 »／« 加入／移除全部搜尋結果；每組相隔 8 DIP，兩組相隔 18 DIP。可用時帶低彩度薄荷綠底，操作說明僅顯示於 Tooltip。✓ 為 26 × 26 DIP，空清單也可完成。
- 目錄優先沿用 `Radio.Core/stations.json` 的 7 台真實電台；可加入與已加入資料分離，`StationLibrary` 可接收擴充目錄。此次不接 Radio Browser API，避免引入未驗證串流與 metadata；未要求手動 URL。
- 移除正在播放的台時，在完成管理後停止並釋放來源；其餘管理或搜尋操作不停止播放。

## 保存與相容性

管理視窗兩邊直接點擊即可選取／取消選取多台，無需按住 Ctrl；另支援 Ctrl+A 全選目前搜尋結果。單箭頭處理選取項目，雙箭頭處理該側所有搜尋結果，搜尋空白時即全部電台。隱藏項目不會被誤加入／移除；左右搜尋更新保留仍可見的已選取項目。主播放器維持單選及原本切台行為。

`radio.settings.json` 繼續使用原本 portable 同目錄、暫存檔後覆寫方式。schema 2 新增：

- `MyStationIds`：依穩定 ID 保存順序；缺少或空陣列表示 0 台。
- `PlayCounts`：電台 ID 對應累積成功播放次數（64 位元整數）。移除後不刪統計，重新加入仍保留。
- schema 1 仍可讀取，保留 Theme／Volume；舊版沒有使用者選台資料，不虛構已加入清單或次數。
- 加入／移除、首次成功播放、偏好調整及結束時保存。唯讀目錄沿用既有行為：當次可操作，但不能持久化；不改寫 AppData／registry。

只有使用者發起的新播放工作階段首次收到真正的 `Playing` 事件才計數。解析失敗、Opening、Buffering、Failed、管理選取與搜尋均不計數；同次工作階段再次收到 Playing 不重複計數。穩定排序只在啟動與關閉管理視窗時執行，不在播放期間移動列。

## 驗證

```powershell
dotnet build src/Radio.Windows -c Release
dotnet run --project tests/Radio.Core.Tests -c Release
dotnet run --project tests/Radio.Windows.Tests -c Release -- --ui
dotnet run --project tests/Radio.Windows.Tests -c Release -- --system
```

UI 測試使用假音訊／metadata，測試設定使用獨立 fixtures，不修改使用者設定。涵蓋搜尋不切台、雙清單轉移／去重／獨立搜尋、modal 所有權、0 台流程、穩定排序、成功／失敗／重複事件計數、設定重新讀取，以及原有音量／metadata／系統匣／DPI 回歸。

截圖保存在 `artifacts/station-*.png`。離線測試與模擬 DPI 訊息不代表各電台即時線上可用或實體多螢幕人工驗收；官方串流及節目資訊可能受來源變更與地區限制影響。
