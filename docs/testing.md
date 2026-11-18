# 分層測試與執行架構

## 日常入口

```powershell
powershell -NoProfile -File scripts/Test.ps1                         # Fast
powershell -NoProfile -File scripts/Test.ps1 -Profile UI
powershell -NoProfile -File scripts/Test.ps1 -Profile Audio
powershell -NoProfile -File scripts/Test.ps1 -Profile Updates
powershell -NoProfile -File scripts/Test.ps1 -Profile Offline        # 完整離線
powershell -NoProfile -File scripts/Test.ps1 -List                   # 列出 profiles 與計畫，不建置
```

在 PowerShell 中可合併 profiles，自動去重：

```powershell
./scripts/Test.ps1 -Profile Fast,Audio
./scripts/Test.ps1 -Group core,hls-recovery,system
./scripts/Test.ps1 -Profile UI -NoBuild
./scripts/Test.ps1 -Profile UI -Screenshots  # 明確需要人工預覽時才保存 PNG
```

`-Group` 與 `-Profile` 不可同時指定。預設 Release；`-Configuration Debug` 可切換。只有確定同一份原始碼已建置過，才能用 `-NoBuild`；不能拿舊產物證明新修改通過。

若本機 execution policy 阻擋腳本，可明確使用 `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test.ps1 -Profile Fast`，只影響該程序，不修改全機／使用者政策。runner 的 PowerShell 子程序同樣只使用程序範圍 Bypass；若組織政策禁止，改依規範申請或手動驗證，不變更全機設定。

## 測試選擇矩陣

| 變更 | 建議執行 |
|---|---|
| 純文件／GitHub 範本 | diff、設定語法、連結；說明未跑程式測試原因 |
| Core／metadata／resolver | Fast |
| UI、電台管理、DPI、焦點 | Fast + UI；必要時實體桌面／多螢幕驗收 |
| 播放策略、備援、音訊緩衝／原生引擎 | Fast + Audio |
| OTA／updater／runtime／發行工具 | Fast + Updates；OTA 入口異動另跑真實套件整合 |
| 測試 runner／共用基礎或廣泛重構 | Offline |
| 電台串流／網路問題 | 上述相關離線項目 + 指定 live 命令 |
| 授權發行 | 完整離線紀錄、Lite/Full OTA 整合與實際封裝驗收 |

這是依風險選擇，不是每次把所有文件中的指令累加。PR 必須記錄實際選擇及未測原因。Fast CI 不代表 UI、音訊或 OTA 已驗收。

## Profiles

唯一編排定義為 `scripts/Test-Plan.ps1`：

- **Fast**：Core、runner 參數契約、純播放策略、診斷、queue/parser、allocation、loopback transport。不操作真實焦點、不播放音訊、不連電台。
- **UI**：shell、電台管理、外觀／模擬 DPI、啟動／指標／啟用焦點、跨程序單例；需可互動 Windows 桌面。焦點與 hover 仍可能受使用者操作干擾（[#64](https://github.com/philliu-212/radio/issues/64)）。
- **Audio**：純策略、queue、transport、有界 RFI 緩衝的原生生命週期、受控 HLS／備援／穩定度、local WAV 系統音訊。全程離線、靜音，但需要可用 Windows 音訊環境。
- **Updates**：離線 OTA fixtures、runtime 檔案驗證器、fake-gh 發行工具回歸、Framework updater 長路徑測試。
- **Tooling**：runner 自測、runtime／發行工具／長路徑回歸；不封裝或發布 Release。
- **Offline**：每個已登錄群組恰好一次，包含上述所有離線範圍。

群組不是測試案例：群組內保留既有 assertion 與 fail-fast 行為，摘要數量不當成 assertion 數量。只有完整 Offline 計畫才可稱完整離線回歸。

## 執行與結果

- 每個選中的 .NET 測試專案只顯式建置一次（Windows 測試的 project reference 同時建置播放器），之後統一 `dotnet run --no-build --no-restore`。MSBuild 仍可做相依專案的增量檢查，並非所有相依專案只有一次 MSBuild invocation。
- 每個群組獨立程序、依序執行。不要平行操作共用 Windows 桌面、焦點、音訊裝置、測試輸出或量測 allocation。
- 每組分開 stdout／stderr log、實際 exit code 與 wall-clock 秒數；預設失敗後繼續其他群組，整體仍回傳非零。`-FailFast` 可提早停止。
- 建置失敗即停止，不執行可能過期的產物；摘要明列 `NotRun`。逾時終止該 runner 啟動的程序樹，狀態 `Timeout`／exit code 124。
- 建置逾時 300 秒；Core 60 秒、Windows 群組 180 秒、腳本 120 秒。這是安全上限，不是效能預算，也不是固定等待。
- 結果寫入新的 `artifacts/test-results/<時間>-<識別碼>/summary.json`，含建置與測試分項。可用 `-ReportDirectory` 指定尚不存在的目錄；不覆寫舊結果。預設只顯示進度、摘要與失敗組 logs；`-Verbose` 於各組結束後顯示所有 logs，完整輸出始終保存。
- 摘要可比較相同機器、Configuration、群組與冷／暖建置的耗時；不可直接比較本機與 hosted runner，或把單次測量當效能保證。
- 設定檔、測試報告、截圖及其他 artifacts 不提交。
- UI 預設不額外產生預覽 bitmap／不保存 PNG；需要像素斷言的 bitmap／原生 PrintWindow 仍在記憶體產生並驗證，不省略斷言。`-Screenshots`（直接 runner 為 `--screenshots`）才額外寫 PNG，摘要記錄是否啟用。截圖契約自測會使用唯一 fixture 路徑驗證開關，這不是 UI 預覽產出。

## 為何目前不改 xUnit／NUnit

現有 console assertions 已有大量 WinForms STA、`Application.Run`、啟動焦點前置與原生音訊 fixture。當前瓶頸是編排及重複範圍，不是 assertion API。為避免遷移造成訊息迴圈／生命週期與平行化回歸，先保留 console runner：

1. `Test-Plan.ps1`：profile、程序隔離、風險分層。
2. `Test.ps1`／`Test-Process.ps1`：建置、逾時、exit code、logs、JSON 摘要。
3. `TestCatalog.cs`：Windows 離線群組與負責的檢查，附逐組計時。
4. `TestCommand.cs`：嚴格命令解析，錯誤在 UI／音訊／網路副作用前拒絕。
5. 既有 `*Checks.cs`：assertions 與 fixtures，不重寫產品。

純離線群組使用 `StaTestContext` 保持 await 前後在同一 STA 執行緒，不顯示隱藏的宿主視窗；需要 WinForms 訊息處理的群組才啟動 `Application.Run`。啟動焦點檢查只由 `ui-focus` 擁有，並在建立測試宿主之前執行。

未來如需 IDE 單案例探索／TRX，可先將純 Core 測試遷移至標準框架；互動 Windows runner 保持獨立。無須為此一次遷移全部測試。

## 直接執行與相容命令

```powershell
dotnet run --project tests/Radio.Windows.Tests -c Release -- --list
dotnet run --project tests/Radio.Windows.Tests -c Release -- --suite ui-shell,ui-stations
dotnet run --project tests/Radio.Windows.Tests -c Release -- --suite playback-policy,hls-recovery
```

`--suite` 只允許離線群組、依指定順序去重。legacy `--ui` 保留全包相容語意，包含外觀、焦點、shell、stations、HLS、純策略、備援與穩定度；不要再接著跑它已涵蓋的群組。`--stability` 仍涵蓋純策略與 MainForm 穩定度。`--system` 或無參數仍驗證原生 local WAV 生命週期；純策略由 `playback-policy` 負責。

legacy 命令一次只能一個，`--ui --system` 現在會失敗，不再默默漏測。未知命令、缺少值、衝突／重複選項均失敗。Core 同樣拒絕未知參數，無參數執行全部 Core 離線檢查。

Live、Edge 探測、`--ota-stage` 等明確命令保留，不進入離線 profile。見 [備援](backup-sources.md)、[音訊緩衝](audio-buffering.md)、[診斷](diagnostics.md) 與 [OTA 驗收](ota-validation.md)。舊 `Smoke-Test.ps1` 的固定六台收藏假設已失效且沒有現行呼叫端，本次移除；其舊版啟動／靜音／tray／退出範圍已有 UI／system 群組，真實 portable 啟動／runtime 檢查由專用 OTA 套件整合負責，不能把 console 測試當成發布包驗收。

### 測項收斂原則

- 移除獨立四箭頭預覽流程，將「只有 hover 的清單顯示對應箭頭」斷言合併至既有左右清單的 hover 端點檢查；不再額外建立預覽視窗、等待兩輪動畫或保存三張預覽。
- Native hover 像素改用同一份記憶體 bitmap；不再先存 PNG、再讀回檢查。tooltip／tray／menu 的像素檢查保留，但預設不存 PNG。
- 保留 queue／parser 的功能與 allocation 測試：測的是正確性與配置成本，不是重複。保留 policy／MainForm／native WAV／live：分別驗證決策、整合、Windows 生命週期與真實來源，不能互相替代。
- Alias 只是相容入口，不新增計畫或測項；完整回歸只跑註冊群組一次。不為了讓計數變小刪掉必要邊界案例或把多組塞成無法定位的大測試。

## CI

`.github/workflows/tests.yml` 在 PR／main push 上執行 Fast，並用離線假程序驗證 runner 的失敗、逾時、去重與報告契約；可手動選擇 Offline，但 hosted runner 的互動 UI／音訊結果不能取代實體環境驗收。結果檔無論成功失敗都上傳。相同 PR 的新提交取消過期執行。

既有 `portable-draft.yml` 只在明確授權的 tag 工作流程中做真實 Lite／Full OTA 套件驗證與發行草稿，不混入 PR 快速檢查。

## 本次導入驗證限制（2026-11-18）

- Fast 多次通過；本機含增量建置約 30 秒，僅作本次環境基準，不宣稱與舊版等範圍的加速百分比。
- Offline 已實際執行兩輪，非全綠：第一輪 21/22 組通過（`ui-stations` 點擊轉移失敗），第二輪 20/22 組通過（`ui-shell` 備選電台加入、`ui-stations` 真實游標 hover 失敗）。兩輪均回傳非零並繼續其他組；音訊、更新、工具與其餘組通過。
- 在乾淨 `origin/main`（90c5d32）對照原始 `--ui`，一次箭頭初始隱藏斷言失敗、另一次通過；新 runner 的 legacy `--ui` 亦曾通過。這支持互動 UI 原有不穩定問題，但**不代表所有點擊失敗都已證實與 #64 同因，也不宣稱拆組後 UI 已完整驗收**。
- #64 保持 open；未增加自動重試、跳過檢查、放寬斷言或要求使用者停止操作滑鼠。UI 仍需進一步隔離真實輸入，不能拿 Fast 綠燈當成 UI 通過。
- 本次未執行線上串流、實體多螢幕、真實 Lite/Full 套件 OTA、正式封裝或 Release。
- 後續收斂完成後，Fast＋Tooling 11/11、`ui-appearance` 不存圖／存圖兩模式通過；最後一輪 Offline（已建置相同來源，`-NoBuild`）21/22 通過。新增的兩側箭頭可見性及記憶體 native pixel 斷言均通過；`ui-stations` 仍在原生列點擊轉移失敗，獨立追蹤 [#128](https://github.com/philliu-212/radio/issues/128)，不混稱為 #64 已知同因。這是已揭露的測試驗收缺口，不以合併代表完整離線全綠。
- 最後保留預覽呼叫原有的 `Refresh` 繪製同步，再跑 UI profile：外觀、焦點、單例 3 組通過；shell 的 esRadio 加入與 stations 的 hover（pointer 已離開列）仍失敗，整體回傳 1。截圖開關不應改變 fixture 的繪製同步需求；上述缺口持續追蹤，不追加反覆重跑來沖淡失敗。
