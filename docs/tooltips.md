# 提示框主題

所有 Radio Mini 提示框跟隨程式內的深／淺色模式，不修改 Windows 全域設定。

- 主視窗與電台管理使用 `ThemedToolTip`：保留 WinForms 的提示文字、定位、尺寸與時序，自繪背景、邊框及文字。
- 系統匣使用 `ThemedTrayIcon`：以 Shell notification icon version 4 的 `NIN_POPUPOPEN`／`NIN_POPUPCLOSE` 顯示／隱藏 `TrayToolTipWindow`，不設定 `NIF_SHOWTIP`，避免原生提示疊在自訂提示上。仍透過 `NIF_TIP` 提供名稱／電台文字給 Shell。
- 系統匣提示不取得焦點、不新增工作列項目、不攔截滑鼠。雙擊與鍵盤選取開啟播放器；右鍵仍顯示原有選單。Explorer 重啟時重新註冊圖示。
- 系統匣提示依所在螢幕 DPI 排版並限制在工作區範圍，包含負座標螢幕；關閉程式時釋放圖示與提示視窗。

## 驗證

```powershell
dotnet build src/Radio.Windows -c Release
dotnet run --project tests/Radio.Windows.Tests -c Release -- --ui
```

離線 UI 測試包含深／淺色繪製、主視窗主題同步、Shell version 4 註冊、模擬提示開關／雙擊／鍵盤選取／選單通知、移除再註冊、資源釋放及工作區邊界計算。測試產生的 `artifacts/*tooltip*.png` 僅供本機檢視，不提交。

模擬通知不等於真實 Shell 懸停驗收。人工驗收需確認：

1. 深／淺色模式下，主視窗、電台管理與系統匣提示均使用對應配色。
2. 將主視窗縮至系統匣，懸停圖示仍能顯示提示；離開即消失，沒有原生淺色提示重疊。
3. 提示顯示時不改變其他程式的鍵盤焦點；雙擊、鍵盤 Enter、右鍵選單及選單動作仍正常。
4. 實體多螢幕／不同 DPI／不同工作列位置／收合圖示區的提示不超出工作區。
5. Explorer 重啟後圖示與提示恢復；關閉播放器後不殘留提示視窗。
