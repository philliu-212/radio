# Radio Mini 圖示

- 最新來源是使用者直接放在專案根目錄的 `radio.png`（1254 × 1254 RGBA），取代先前分享頁預覽。原始檔保留原狀；正式資產 `RadioMini.png` 為逐位元相同的副本，不再自行去背、轉色、裁切或改變構圖。
- 來源及 `RadioMini.png` SHA-256 均為 `5F8A8AE795B4CB8D8D26354A3BCC9AA3EC23F3C0B6C35AAF935CC4573DC52DA5`。僅依使用者指定作為應用程式圖示，不宣稱擁有圖中人物或其他權利。
- `RadioMini.ico`：16、20、24、32、40、48、64、128、256 px 的 32-bit PNG frames，供 `RadioMini.exe`、Windows 視窗／工作列與系統匣使用。補上 40px，避免 125% 縮放時用 32px 再放大；主視窗自繪標題列的既有 Radio 向量圖示不改。
- 依後續「工作列／系統匣主體太小」回饋，**只在 ICO 修整外圍透明／近透明留白**：以 alpha ≥ 8 找到可見線稿界限，保護一個來源像素的抗鋸齒邊緣，等比例置中至約 96% 畫布（至少留半個輸出像素）。本來源框為 `(102,179,1071,997)`。正式 PNG 與原檔仍逐位元相同、不裁切。
- 框內直接保留來源 RGBA 做高品質縮放，不從亮度重建、不刪黑色、不轉白、不改線條不透明度，也不加底色／描邊。小尺寸自然會失去部分線稿細節；白色線稿的視覺重量不會等同 Teams 等實心彩色圖示。
- 視窗／工作列按目前 DPI 選 32 DIP frame，系統匣另外選 16 DIP frame，不再共用同一個 32px HICON 交給系統縮小；DPI 改變時換圖並釋放舊 Icon。模擬 DPI 回歸不替代實體混合 DPI 多螢幕驗收。
- 來源含大量非常低的 alpha（例如 A=1）；PNG 完整保留這些像素，ICO 框內也使用原 alpha，不把它們當成不透明白色或用閾值改成透明。框外僅作留白修整。一般圖片預覽器顯示可能不同，應以正確 alpha 合成至深／淺底查看。
- 深／淺背景預覽及測試產物只存外部暫存目錄，不提交。根目錄的使用者檔案 `radio.png` 不改寫、不另外暫存進 Git；正式副本已在此目錄保留完整來源。

若需從使用者原檔重建，可在已信任的 PowerShell 工作階段執行（不修改全機執行政策）：

```powershell
& ./scripts/Convert-AppIcon.ps1 -SourcePath ./radio.png
```

沒有根目錄原檔的 clone 也能用 `-SourcePath ./src/Radio.Windows/Assets/RadioMini.png` 重建 ICO，PNG 會維持原狀。腳本只用 Windows System.Drawing，不增加播放器執行依賴；來源必須已含透明背景，不會替一般照片猜測去背。UI 回歸會核對完整 PNG 雜湊，並逐像素比較九個 ICO frames 與框選縮放結果。

## EXE 更新與診斷

`Run.cmd` 會建置並執行 Debug 的 `RadioMini.exe`；不要誤點舊版 `Radio.exe` 或尚未更新的 portable 包。更名不會刪除舊建置、終止舊程序或變更 portable 設定位置。工作列／原生視窗名稱只有 Radio Mini；版號僅留在品牌懸浮提示及系統匣版本資訊。

圖示測試直接讀取 EXE 自己的 Win32 `RT_GROUP_ICON`／`RT_ICON` 資源，逐 frame 比對目前 ICO，不只依賴可能快取的 Shell 擷取結果。可只做圖示診斷（不連線電台、不啟動一般播放器）：

```powershell
dotnet run --project tests/Radio.Windows.Tests -c Release -- --icons --exe C:/path/RadioMini.exe
```

若內嵌資源正確但 Explorer／釘選仍顯示舊圖，可能是舊路徑／Shell 快取；不自動清全系統快取、重啟 Explorer 或修改釘選。本次更名使用新檔案路徑，舊包仍需使用者另行更新，不能把未重建的舊檔宣稱為新版。
