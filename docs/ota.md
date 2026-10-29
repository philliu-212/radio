# Radio Mini 背景 OTA 實作與邊界

## 現況調查與採用方式

- 原 app：`Radio.Windows`，.NET 8、WinForms、x64 Windows 10 2004+；核心／音訊／metadata 不重寫。
- 原 Lite framework-dependent、Full self-contained，皆多檔 apphost；EXE／DLL／runtime 必須保持 publish 相對關係。新方案把**整套 publish** 放進同一 hash 目錄，WorkingDirectory 也設定為該目錄，不搬單一 EXE。
- 原入口 `Program.Main` 支援 `--probe`、音訊 backend 與 `--no-autoplay`；原本沒有單實例保護，設定在 AppContext.BaseDirectory 的 `radio.settings.json`。
- 原打包是完整資料夾 ZIP，GitHub repo 為 public；舊方案逐檔 GZip 已撤除，舊 v0.5.0 已退回 draft，現有 tag 未擅自移動。
- 新設定仍採原 `ThemeSettings` schema 1／2、`StationLibrary`、電台 ID、播放偏好／次數；只改資料路徑。開發／probe 不受 Launcher／OTA 限制，開發設定仍在原位置。

參考官方原始碼（研究時 main，並非宣稱逐行移植）：

- [updateService.win32.ts](https://github.com/microsoft/vscode/blob/main/src/vs/platform/update/electron-main/updateService.win32.ts)：完整 setup 背景下載、checksum、Downloaded／Updating／Ready、等待 ready mutex、使用者重啟。
- [code.iss](https://github.com/microsoft/vscode/blob/main/build/win32/code.iss)：VersionedResourcesFolder、背景資源安裝、穩定入口與重啟配合。
- [inno-updater](https://github.com/microsoft/inno-updater)：持有／等待舊程序、版本化資源切換與清理。

Radio Mini 不採 Electron、Inno Setup 或需要另附 runtime 的大型 updater；選 Windows 內建 .NET Framework 4.x 編譯的穩定 Launcher，與 .NET 8 更新器共用 C# 5 相容的 `PortableStore.cs` 協定。Windows Framework compiler 只在建置需要；執行只需 OS 內建 Framework。Launcher 帶原 Icon，原 `.NET 8` apphost／相依保留在 hash 目錄；外部捷徑必須指向根入口。

## 狀態及 Latest Wins

`data/ota/state.json` 有 Protocol、Current、Pending、Previous、Trial，各 package 記完整 40 碼 commit／版本／模式／檔案清單。hash 目錄採 commit 前 12 碼；前綴碰撞會核對完整 identity，拒絕覆寫。

1. 最新穩定版本數值比較（不含 draft／prerelease），只選同模式單一 ZIP，大小上限 400 MB。
2. `latest <= current` 不下載；`latest <= pending` 在背景驗證既有 Pending，直接 Ready，不重抓。
3. 同時間只有一個檢查與一個準備任務；跨程序有 `update.lock`。較高版本取消舊任務，取消完成才開始另一個，不累積佇列。
4. 新包完整驗證／安全解壓到同磁碟 staging，逐檔 SHA-256 與長度驗證成功後才搬到根 hash 目錄。只有此時才原子替換 Pending。
5. 較高版本失敗，保留原 Current 與已備妥 Pending。下載中的 partial／解壓中斷不影響可用版本；孤兒 staging 在下次準備／Launcher 啟動時清理。
6. 清理只針對有合法 package identity 的 hash 資源目錄，保留 Current／Pending／Previous；不碰 data 自建檔、任意其他根檔或未辨識目錄。

此設計允許 1.0 → 1.2／1.3 直接更新，與已經安裝的每個中間版本無關。換個 app 實例仍能辨識 state.Pending。正式模式每 6 小時檢查，所以「下載途中較高版本」在下一次檢查才會被發現，不是 GitHub push 即時通知。

## 快速切換與回復

Launcher 取得 `launch.lock`、檢查 `run.lock`，重啟請求最多等舊 owner 30 秒，並持有舊程序 handle 等待退出。一般重複啟動不開第二個資料 owner，盡力還原原視窗。直接啟動 hash 內的播放器會轉交根 Launcher；child 才持有整個播放生命週期的 `run.lock`。

在沒有舊程序的狀況下，Launcher 取得 update lock，備份設定至 `data/ota/settings.rollback`，以同磁碟暫存檔＋Flush(true)＋File.Replace／File.Move 更新 state；先記 Previous／Trial，再啟動 Current。背景已驗證完整 package，切換時不重新雜湊數百個 runtime 檔案、不下載、不解壓。Ready handshake 使用每次啟動隨機檔案；主視窗進入訊息迴圈後才寫，Launcher 確認後清除 Trial。清理在新視窗已啟動後進行，不納入切換關鍵路徑。

無法啟動／提早結束／30 秒沒有握手：僅終止**此次啟動的新 child**、還原原設定與 Previous，啟動舊版並保存 last-error。無法取得鎖／舊程序不退出，不切換、不殺使用者的既有實例。斷電留下 Trial 時，下次 Launcher 保守回復上一版。state 主檔損壞可讀 `.bak`；兩份都損壞則明確提示並保留資料，**不自行猜測某個目錄是最新版**。

健康握手只證明視窗已啟動，不保證後續長時間運行／音訊健康。握手之後才崩潰不是自動 rollback 觸發條件。File.Replace 的原子性不是跨所有檔案、磁碟快取／硬體毀損的絕對斷電保證；需在本機可寫檔案系統，網路共享／不支援原子替換的檔案系統未驗收。

## 資料遷移及相容性

- 首次用獨立新資料夾，不原地覆蓋舊 `.exe`／runtime。`Migrate-Portable.ps1` 驗證程序已關閉、拒絕來源／目的連結和覆蓋既有設定，按位元組複製／核對 SHA-256，原包保留。
- 亦可把舊設定複製到新根目錄，Launcher 只在 data 尚無設定時複製，保留原檔。
- 本期 OTA 協定只接受 `DataSchema=2` 與 `Protocol=1`，原 schema 1／未標版本的 legacy 設定仍由現有 loader 遷移；不支援的較新設定 schema 明確停止，不用預設值覆蓋。
- 尚不實作任意、不可逆的資料格式升級。未來破壞性 schema／Launcher 協定變動必須設計新遷移或手動升級，不能讓舊 Launcher 直接套用。每次試啟前備份設定；失敗回復時會回復到切換前快照。其他自建 data 檔不修改。

## 信任與發布

Repo 為 public，程式只對固定 GitHub repository 的 HTTPS latest API／asset URL 發出匿名請求。不含 PAT、GitHub CLI token 或其他長期憑證。private repo 的 404 無法匿名繞過：必須改為公開的發行資產 repo，或另建具使用者登入與短效簽名 URL 的服務，**不能直接把 CI token 放進 app**；本次沒有這種服務。

CI／本機自動產生 SHA256SUMS。執行時以 GitHub API ZIP 的 `sha256:` digest 作為受 HTTPS 保護的完整包雜湊；包內清單被 ZIP digest 一起覆蓋，再核對全部檔案。若 API 沒 digest 就拒絕，並非從同一 ZIP 自行取一個不受外部信任保護的 hash。這能偵測毀損，不是開發者簽章；尚無 Authenticode／離線更新簽章金鑰，惡意發布者或 GitHub 帳號被盜不在 checksum 的保護範圍。

ZIP 拒絕路徑穿越／根路徑／ADS／保留裝置名／重複檔案／連結／不在清單中的額外資源／使用者設定。限制 entry 數、單檔大小、總解壓量與實際寫入量，拒絕版本／模式／commit／資料協定不符。目的路徑拒絕 junction／symlink；不對任意路徑做廣泛刪除。

正式 Release 只放 Lite ZIP、Full ZIP、SHA256SUMS 三項。維持 draft 到使用者確認，不自動合併／移動既有 tag／刪除既有草稿資產。目前舊 v0.5.0 tag 與草稿仍是歷史逐檔建置，**不得重新公開該草稿冒充新 OTA**；重新發行前須另行確認 tag／資產處理。

## 測試分類

- `--updates`：小型離線假包，覆蓋 Latest Wins、取消、去重、退避、中斷／雜湊／ZIP／資料 schema、防破壞 pending、鎖與原子狀態回復；不代表真實 runtime 或網路效能。
- `New-OtaTestPackages.ps1`＋`Test-PortableUpdate.ps1`：真實 publish 的 1.0／1.1／1.2 WinForms 包，使用明確標示的測試 identity（不發行），完整 ZIP 透過 loopback TCP/HTTP 傳輸；保持 1.0 視窗不退出依序準備 1.1、1.2，無重複下載、直接切換 1.2、驗證磁碟／設定、重複入口與壞 EXE 啟動回復。
- `--no-update-check` 僅診斷用，避免隔離測試連真實 GitHub；預設不帶此參數會啟動／定期檢查。`--no-autoplay` 在 Launcher 重啟時保留。測試空收藏，不會假稱已聽到廣播。
- 正式 GitHub 上新協定 ZIP 的 WAN 下載、實體斷電、磁碟滿／毀損、網路分享磁碟、任意安全軟體政策、人工有聲播放／長時間／多螢幕，均需另外驗收。這次不公開新 Release 來假裝已完成。

實際量測另見 [ota-validation.md](ota-validation.md)，不把記憶體 fixture 時間寫成正式網路與重啟時間。
