# Windows Portable 與背景 OTA

播放器仍是 .NET 8 WinForms（Windows 10 2004+／Windows 11 x64，需媒體元件）；不是 single-file、不含 mpv／FFmpeg。

| 包 | 播放器相依 | 啟動 |
| --- | --- | --- |
| Lite | 已安裝 .NET 8 Desktop Runtime x64 | 最外層 `RadioMini.exe` |
| Full | hash 目錄內附 .NET runtime | 同上，保留整個資料夾 |

根 EXE 是真正的 .NET 8 原生 apphost，直接載入 hash 內 RadioMini.dll，圖示、產品名稱、描述與版本均屬 Radio Mini；沒有內層同名 EXE 或另一個 Launcher 版號。獨立 updater 只在切換時使用 Windows 內建 .NET Framework 4.x，不另帶 runtime、不需安裝或管理員權限。Full 從自己的 hash 目錄載入 runtime。Windows N 仍需 Media Feature Pack。資料夾須可寫；不改 AppData／registry 存放偏好。

```text
Radio-Portable-v0.5.0-Full-win-x64-時間/
  RadioMini.exe                 # 真正主程式，捷徑指向這裡
  bin/
    updater.exe                 # 只在切換時執行
  READ-ME.txt
  THIRD-PARTY-NOTICES.txt
  0123456789ab/                  # Git commit 前 12 碼，不是版號目錄
    RadioMini.dll
    RadioMini.deps.json
    RadioMini.runtimeconfig.json
    package.json                # 完整 commit、版本、模式、協定、資料 schema、檔案雜湊
    ...                         # 原生相依、Full runtime、授權文件
  data/
    radio.settings.json
    ota/
      state.json                # Current／Pending；Previous／Trial 僅試啟期
      state.json.bak
      ...                       # lock、診斷、資料回復備份與 staging，全在此處
```

## 建置與發行

版號採 `major.feature.fix`：新增功能升第二碼，修正升第三碼。版號以 `src/Radio.Windows/Radio.Windows.csproj` 為準。Full runtime 來源／版本以 `scripts/runtime-package.json` 為準；**.NET 8 於 2026-11-10 結束支援**，長期版本需另外升級，不把此 OTA 工作宣稱為 runtime 支援年限已解決。

- 平常 `Run.cmd`／`dotnet run` 保持原開發流程，不包版、不檢查 OTA。
- 明確授權封裝後，先提交程式碼，再用 `Release.cmd`，或分別執行下列指令。不得有未提交的 tracked 修改；版本目錄名稱來自實際 `git rev-parse HEAD`。
- `Publish-Release.ps1` 分別 publish Lite／Full 到 `artifacts/publish-payload/`；`New-PortableLayout.ps1` 產生 hash 資源目錄、Files／EntryFiles 清單及初始 state，`Build-AppHost.ps1` 呼叫 SDK HostWriter 產生根入口（相對 hash/DLL），`Build-Updater.ps1` 以 Windows Framework compiler 編譯切換工具，最後產生完整 ZIP 與 `SHA256SUMS-v版本.txt`。不覆蓋舊資料夾或設定。

```powershell
powershell -NoProfile -File scripts/Publish-Release.ps1
powershell -NoProfile -File scripts/Publish-Release.ps1 -SelfContained
```

Release **只有兩個完整 ZIP 與一份 SHA256SUMS**，不逐檔發布。ZIP 就是手動安裝與 OTA 共用的包，包含一個最外層資料夾。不提交 `dist/`／`artifacts/` 或使用者設定。

`.github/workflows/portable-draft.yml` 只能手動觸發，對已授權建立的 tag 建置、測試，再呼叫 `Publish-GitHubRelease.ps1` 建立 **draft**；不自行建立／移動 tag、不公開 Release。若既有 tag／draft 指向別的建置或含舊協定資產，必須先由人確認如何處理，不會偷偷覆蓋。正式公開前核對兩個 ZIP／SHA256SUMS、GitHub ZIP `sha256:` digest，再另行授權設為 latest。

封裝保留 Lite／Full readme、`THIRD-PARTY-NOTICES.txt`，Full 另保留實際 runtime 原始 LICENSE／NOTICE。不能以縮小體積為由刪除。

## 一鍵更新

啟動及每 6 小時檢查 GitHub 最新穩定 Release。背景下載完整同模式 ZIP，核對 API 提供的 SHA-256／大小，在 `data/ota/staging/隨機值/` 解壓、驗證全部清單檔案，再將 hash 資源目錄搬至 portable 根目錄、原子記錄 Pending。這些工作都在使用者仍可收聽時完成。

右上角沿用原箭頭，懸浮顯示背景下載百分比／準備中／已備妥／失敗重試；沒有大型更新視窗，不強制退出。Ready 時才提供「重新啟動並更新」。若仍在準備更高版本而已有較舊 Ready，可取消新任務並先套用已備妥者。失敗從約 15 秒開始指數退避，最高 15 分鐘；UI 輪詢每 30 秒，實際重試可能晚於計算時間。可點失敗箭頭手動重試。

重新啟動時 updater 從暫存副本執行，等待原程序完全結束、備份小型設定與兩個入口、原子記錄 Trial、替換根 EXE／updater，再啟動根主程式；不下載、不解壓、不雜湊整套 runtime。主視窗健康握手成功後自動刪除舊版及回復備份，不長期保留 Previous。未握手／程序提早退出／無法啟動則回復入口、上一版及設定；握手逾時 30 秒。正常退出後再開啟根主程式，也會請 updater 套用 Ready。無常駐 Launcher，跨斷電的自癒限制見 ota.md。

Latest Wins、首次遷移、安全性、資料相容性、參考來源及完整測試範圍見 [ota.md](ota.md)。

## 首次從舊平面 Portable 升級

**正式 v0.4.0 沒有更新器，舊逐檔 v0.5.0 草稿也不支援新協定，不能假裝它們會自動遷移。**

1. 關閉舊版的所有實例，將新 ZIP 解壓到**另一個新資料夾**，不原地覆蓋執行中的 EXE／DLL。
2. 在專案內執行 `Migrate-Portable.ps1 -OldDirectory <舊資料夾> -NewDirectory <新資料夾>`。只複製並驗證既有設定，原資料夾不動；目的已有設定、程序仍在執行、資料 schema 不支援或含連結時拒絕覆蓋。
3. 沒有腳本時，亦可將舊 `radio.settings.json` 複製到新包根目錄；首次主程式會複製到 `data/`，保留原檔。目的 `data/radio.settings.json` 已存在時不覆蓋。若來源是先前未發行的 Launcher 候選，請改複製其 `data/radio.settings.json`，不要把舊 OTA state 搬進新包。
4. 捷徑指向新包**最外層** `RadioMini.exe`，不要釘選／直接指定某個 hash 子目錄。確認收藏、音量與主題後，再自行處理舊包。

## 驗證

離線 console 測試用 `dotnet run`，不是 `dotnet test`：

```powershell
dotnet build src/Radio.Windows -c Release
dotnet run --project tests/Radio.Core.Tests -c Release
dotnet run --project tests/Radio.Windows.Tests -c Release -- --ui
dotnet run --project tests/Radio.Windows.Tests -c Release -- --system
dotnet run --project tests/Radio.Windows.Tests -c Release -- --updates
```

實際 portable／獨立 updater／跨版測試（只建立隔離測試資料，不發行）：

```powershell
powershell -NoProfile -File scripts/New-OtaTestPackages.ps1 -Mode Lite
powershell -NoProfile -File scripts/New-OtaTestPackages.ps1 -Mode Full
# 使用上一步輸出的 1.0.0／1.1.0／1.2.0 測試 ZIP：
powershell -NoProfile -File scripts/Test-PortableUpdate.ps1 -BaselineZip <1.0.0.zip> -CandidateZip <1.1.0.zip> -SupersedingZip <1.2.0.zip>
```

整合腳本使用真實根 WinForms apphost／updater 及 loopback HTTP 的完整 ZIP，保持舊程序運行直到重啟，驗證 Latest Wins、沒有重複下載、程序鎖、兩入口與設定回復、成功刪舊版、根 EXE 名稱／版本及 Full runtime；結果與量測在 `artifacts/ota-integration-*/result.json`。這不是 GitHub WAN 實測，也不代替人工聽音、長時間、睡眠／斷網或實體多螢幕驗收。
