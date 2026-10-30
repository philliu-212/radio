# Radio Mini 背景 OTA 實作與邊界

## 現況調查與採用方式

- 原 app：`Radio.Windows`，.NET 8、WinForms、x64 Windows 10 2004+；核心／音訊／metadata 不重寫。
- Lite framework-dependent、Full self-contained，皆多檔 apphost。根 `RadioMini.exe` 由 SDK `HostWriter.CreateAppHost` 產生，嵌入相對 `<hash>/RadioMini.dll`，複製該 DLL 的原圖示／Radio Mini 名稱／版本資源；hash 內不再保留 publish 的 `RadioMini.exe`。不是自行 patch bytes，也不是另一個 managed Launcher。
- .NET 8 官方 [corehost.cpp](https://github.com/dotnet/runtime/blob/v8.0.0/src/native/corehost/corehost.cpp) 以 embedded managed DLL 的目錄作為 app_root；Full 的 hostfxr／coreclr 與相依 DLL 因此保持在同一 hash 目錄。Lite 使用系統 Desktop Runtime。工作目錄設為資源目錄，但不靠工作目錄猜測 DLL 路徑。
- 原入口 `Program.Main` 支援 `--probe`、音訊 backend 與 `--no-autoplay`；原本沒有單實例保護，設定在 AppContext.BaseDirectory 的 `radio.settings.json`。
- 原打包是完整資料夾 ZIP，GitHub repo 為 public；舊方案逐檔 GZip 已撤除，舊 v0.5.0 已退回 draft，現有 tag 未擅自移動。
- 新設定仍採原 `ThemeSettings` schema 1／2、`StationLibrary`、電台 ID、播放偏好／次數；只改資料路徑。開發／probe 不受 Portable OTA 限制，開發設定仍在原位置。

參考官方原始碼（研究時 main，並非宣稱逐行移植）：

- [updateService.win32.ts](https://github.com/microsoft/vscode/blob/main/src/vs/platform/update/electron-main/updateService.win32.ts)：完整 setup 背景下載、checksum、Downloaded／Updating／Ready、等待 ready mutex、使用者重啟。
- [code.iss](https://github.com/microsoft/vscode/blob/main/build/win32/code.iss)：VersionedResourcesFolder、背景資源安裝、穩定入口與重啟配合。
- [inno-updater](https://github.com/microsoft/inno-updater)：持有／等待舊程序、版本化資源切換與清理。

2026 年研究時的官方 main：VS Code user 安裝版背景執行完整 Setup，等待 ready mutex，重啟時移除旗標放行切換；Archive 版只提供下載，並非相同自動安裝。RadioMini 借鑑預部署／Ready／獨立 helper／切換／GC，而不宣稱逐行移植或與 VS Code ZIP 相同。

Radio Mini 不採 Electron／Inno Setup。唯一根入口是真正的 .NET 8 原生 apphost；`bin/updater.exe` 為 Windows 內建 .NET Framework 4.x 的小型工具，共用 C# 5 相容 `PortableStore.cs`。只在切換時執行，沒有播放器視窗，不常駐，不另帶 runtime。所有捷徑繼續指向根 `RadioMini.exe`。

Updater 不嵌入 Radio Mini 的應用程式圖示，避免與播放器混淆。`bin/` 是內部工具共用目錄，不是 updater 專屬子目錄；未來有實際需要時可放其他 helper，不預先新增空目錄。維持 EXE 而非 PowerShell／批次腳本，以避免執行政策、Shell 引數轉義與主控台視窗干擾，並可靠地處理程序等待、檔案鎖與交易回復；這不是程式碼簽章或不被安全軟體攔截的保證。

## 狀態及 Latest Wins

`data/ota/state.json` 有 Protocol、Current、Pending、Previous、Trial，各 package 記完整 40 碼 commit／版本／模式／檔案清單。hash 目錄採 commit 前 12 碼；前綴碰撞會核對完整 identity，拒絕覆寫。

1. 最新穩定版本數值比較（不含 draft／prerelease），只選同模式單一 ZIP，大小上限 400 MB。
2. `latest <= current` 不下載；`latest <= pending` 在背景驗證既有 Pending，直接 Ready，不重抓。
3. 同時間只有一個檢查與一個準備任務；跨程序有 `update.lock`。較高版本取消舊任務，取消完成才開始另一個，不累積佇列。
4. 新包完整驗證／安全解壓到同磁碟 staging，逐檔 SHA-256 與長度驗證成功後才搬到根 hash 目錄。只有此時才原子替換 Pending。
5. ZIP 同時驗證兩個 EntryFiles：根 apphost 與 updater，保存到 `data/ota/entries/<hash>/app.bin`／`updater.bin`；沒有第二個可點擊播放器入口。
6. 較高版本失敗，保留原 Current 與已備妥 Pending。下載中的 partial／解壓中斷不影響可用版本；孤兒 staging 在下次準備／切換時清理。新 Pending 成功提交後清理舊 Pending 資源及入口暫存。
7. 清理只針對有合法 package identity 的 hash 資源目錄；Previous 僅在 Trial 期間保留，健康成功後刪除；不碰 data 自建檔、任意其他根檔或未辨識目錄。

此設計允許 1.0 → 1.2／1.3 直接更新，與已經安裝的每個中間版本無關。換個 app 實例仍能辨識 state.Pending。正式模式每 6 小時檢查，所以「下載途中較高版本」在下一次檢查才會被發現，不是 GitHub push 即時通知。

## 快速切換與回復

正常啟動直接進入根主程序，持有 `run.lock`，不執行 updater。重複啟動不建立第二個資料 owner。下次啟動發現 Pending／未確認 Trial，才提出切換請求並退出；或由執行中的 UI 選擇重新啟動並更新。

Updater 前端複製自己到 `data/ota/workers/<guid>/updater.exe`，啟動 worker 後退出，避免更新自己的鎖檔。Worker 依 PID＋啟動時間等舊主程序結束，取得 launch／update 鎖，確認 run lock 可用，備份小型設定與兩個入口；先原子記錄 Previous／Trial，再以同磁碟 Flush(true)＋File.Replace 替換根 EXE 與 bin/updater.exe，啟動根主程式。切換只驗證兩個小型入口，不重新雜湊整套 runtime、不下載、不解壓。

Ready handshake 使用每次啟動隨機檔案；主視窗進入訊息迴圈後才寫。健康成功後清空 Previous／Trial，連續保存兩次以避免 `.bak` 復活已刪版本，再清除舊資源與回復入口／設定備份。暫時檔案占用只有限重試，不關閉安全軟體。正在執行的 worker 暫存本身不強制自刪；超過一天的暫存副本在後續正常啟動時回收，鎖住的保留，不殺程序。

替換失敗／無法啟動／提早結束／30 秒沒有握手：僅終止此次啟動的新主程序，還原兩個入口、原設定與 Previous，重新啟動根舊版並保存 last-error。無法取得鎖／舊程序不退出，不切換、不殺使用者的既有實例。斷電留下 Trial 時，下次可進入 managed 初始化的根主程序請 updater 保守回復。state 主檔損壞可讀 `.bak`；兩份都損壞則明確提示並保留資料，不自行猜最新版。

限制：若斷電／磁碟毀損使新 apphost 在 managed 初始化前就無法載入 DLL 或 runtime，主程式沒有機會自動請求回復；需手動解壓已知良好 Portable。首次試啟時的 updater 可偵測這類失敗並回復，但不能保證機器斷電後仍自動運行。這是無常駐 launcher／安裝服務設計的邊界，不宣稱任意斷電皆可自癒。

健康握手只證明視窗已啟動，不保證後續長時間運行／音訊健康。握手之後才崩潰不是自動 rollback 觸發條件。File.Replace 的原子性不是跨所有檔案、磁碟快取／硬體毀損的絕對斷電保證；需在本機可寫檔案系統，網路共享／不支援原子替換的檔案系統未驗收。

## 資料遷移及相容性

- 首次用獨立新資料夾，不原地覆蓋舊 `.exe`／runtime。`Migrate-Portable.ps1` 驗證程序已關閉、拒絕來源／目的連結和覆蓋既有設定，按位元組複製／核對 SHA-256，原包保留。
- 亦可把舊設定複製到新根目錄，主程式只在 data 尚無設定時複製，保留原檔。
- 本期 OTA 協定只接受 `DataSchema=2` 與 `Protocol=1`，原 schema 1／未標版本的 legacy 設定仍由現有 loader 遷移；不支援的較新設定 schema 明確停止，不用預設值覆蓋。
- 尚不實作任意、不可逆的資料格式升級。未來破壞性 schema／updater 協定變動必須設計新遷移或手動升級。每次試啟前備份設定；失敗回復時回復到切換前快照。其他自建 data 檔不修改。未發布的 Launcher 候選不視為此新 EntryFiles 協定的相容來源。

## 信任與發布

Repo 為 public，程式只對固定 GitHub repository 的 HTTPS latest API／asset URL 發出匿名請求。不含 PAT、GitHub CLI token 或其他長期憑證。private repo 的 404 無法匿名繞過：必須改為公開的發行資產 repo，或另建具使用者登入與短效簽名 URL 的服務，**不能直接把 CI token 放進 app**；本次沒有這種服務。

CI／本機自動產生 SHA256SUMS。執行時以 GitHub API ZIP 的 `sha256:` digest 作為受 HTTPS 保護的完整包雜湊；包內清單被 ZIP digest 一起覆蓋，再核對全部檔案。若 API 沒 digest 就拒絕，並非從同一 ZIP 自行取一個不受外部信任保護的 hash。這能偵測毀損，不是開發者簽章；尚無 Authenticode／離線更新簽章金鑰，惡意發布者或 GitHub 帳號被盜不在 checksum 的保護範圍。

ZIP 拒絕路徑穿越／根路徑／ADS／保留裝置名／重複檔案／連結／不在清單中的額外資源／使用者設定。限制 entry 數、單檔大小、總解壓量與實際寫入量，拒絕版本／模式／commit／資料協定不符。目的路徑拒絕 junction／symlink；不對任意路徑做廣泛刪除。

正式 Release 只放 Lite ZIP、Full ZIP、SHA256SUMS 三項。維持 draft 到使用者確認，不自動合併／移動既有 tag／刪除既有草稿資產。目前舊 v0.5.0 tag 與草稿仍是歷史逐檔建置，**不得重新公開該草稿冒充新 OTA**；重新發行前須另行確認 tag／資產處理。

## 測試分類

- `--updates`：小型離線假包，覆蓋 Latest Wins、取消、去重、退避、中斷／雜湊／ZIP／資料 schema、防破壞 pending、鎖與原子狀態回復；不代表真實 runtime 或網路效能。
- `New-OtaTestPackages.ps1`＋`Test-PortableUpdate.ps1`：真實 publish 的 1.0／1.1／1.2 WinForms 包，使用明確標示的測試 identity（不發行），完整 ZIP 透過 loopback TCP/HTTP 傳輸；保持 1.0 視窗不退出依序準備 1.1、1.2，無重複下載、直接切換 1.2、驗證磁碟／設定、重複入口與壞 EXE 啟動回復。
- `--no-update-check` 僅診斷用，避免隔離測試連真實 GitHub；預設不帶此參數會啟動／定期檢查。`--no-autoplay` 在重啟時保留。測試空收藏，不會假稱已聽到廣播。
- 正式 GitHub 上新協定 ZIP 的 WAN 下載、實體斷電、磁碟滿／毀損、網路分享磁碟、任意安全軟體政策、人工有聲播放／長時間／多螢幕，均需另外驗收。這次不公開新 Release 來假裝已完成。

實際量測另見 [ota-validation.md](ota-validation.md)，不把記憶體 fixture 時間寫成正式網路與重啟時間。
