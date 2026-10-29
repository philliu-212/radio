# Radio Mini 圖示

- 最新來源是使用者直接放在專案根目錄的 `radio.png`（1254 × 1254 RGBA），取代先前分享頁預覽。原始檔保留原狀；正式資產 `RadioMini.png` 為逐位元相同的副本，不再自行去背、轉色、裁切或改變構圖。
- 來源及 `RadioMini.png` SHA-256 均為 `5F8A8AE795B4CB8D8D26354A3BCC9AA3EC23F3C0B6C35AAF935CC4573DC52DA5`。僅依使用者指定作為應用程式圖示，不宣稱擁有圖中人物或其他權利。
- `RadioMini.ico`：16、20、24、32、48、64、128、256 px 的 32-bit PNG frames，供 EXE、Windows 視窗／工作列與系統匣使用。主視窗自繪標題列的既有 Radio 向量圖示不改。
- 只對 ICO 各尺寸做保留 RGBA 的高品質等比例縮放。直接使用來源 alpha，不從亮度重建、不刪黑色、不強制變白或增加不透明度，也不裁切或重新配置內容；非正方形來源只增加透明留邊。小尺寸自然會失去部分線稿細節；白色圖案在淺色底對比較低，不擅自加底色或外框。
- 來源含大量非常低的 alpha（例如 A=1）；這些近透明像素同樣保留，不得視為不透明白色，亦不得直接刪掉。一般圖片預覽器顯示可能不同，應以正確 alpha 合成至深／淺底查看。
- 深／淺背景預覽及測試產物只存外部暫存目錄，不提交。根目錄的使用者檔案 `radio.png` 不改寫、不另外暫存進 Git；正式副本已在此目錄保留完整來源。

若需從使用者原檔重建，可在已信任的 PowerShell 工作階段執行（不修改全機執行政策）：

```powershell
& ./scripts/Convert-AppIcon.ps1 -SourcePath ./radio.png
```

沒有根目錄原檔的 clone 也能用 `-SourcePath ./src/Radio.Windows/Assets/RadioMini.png` 重建 ICO，PNG 會維持原狀。腳本只用 Windows System.Drawing，不增加播放器執行依賴；來源必須已含透明背景，不會替一般照片猜測去背。UI 回歸會核對完整 PNG 雜湊，並逐像素比較八個 ICO frames 與來源縮放結果。
