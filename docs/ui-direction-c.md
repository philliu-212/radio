# Radio Mini — Direction C

依使用者 2026-10-28 提供的方向 C 圖與 v1.0 規格實作，不沿用先前的直式草稿。

v0.3.1 依後續要求調整互動：點選直接開播／切台、每頁五台與細 overlay 捲動條、依頻率數字排序、保存音量並修正滑桿點擊。這些要求取代初版的「只選取、不立即播放」。每列維持原本 37 DIP，不等距拉長；整體高度從 270 縮為 233 DIP，配色、字級與左右欄配置不變。

## 視覺與元件

- 預設 550 × 233 DIP，左／右欄按 46:54 分配（不含 8 DIP 欄間距），保留頂部自訂標題列。
- 深色配色對應指定 HEX：背景 `#202225`、卡片 `#292D31`、列 `#2A2D31`、播放列 `#293A35`、選取列 `#394A49`、薄荷綠 `#9BE0C6`、主按鈕 `#A6DFC9`。淺色只換色、不換版面。
- 系統字型：清單 10 pt、標題 11 pt、頻率 16 pt；DPI 改變時按相同 DIP 字級重建／快取字型，不靠縮字塞內容。頻率列保留足夠行高，以免 125% 以上裁字。
- 使用原生 `ListBox`、`Button`、`CheckBox`、`TrackBar`，搭配少量 GDI+ 圓角／向量圖示。不引入套件、WebView、陰影引擎、動畫或跑馬燈。
- 清單每頁五台，每列維持 37 DIP（包含原有間距），縮短視窗而不拉長列高、不縮字、不改左右欄寬。保留原生 ListBox 選取／鍵盤機制，隱藏佔位的原生捲動條，右緣繪製 3 DIP 低對比 overlay thumb；可拖曳、點擊、滾輪、Home／End／方向鍵捲動，不另設捲動欄。
- 元件有可見鍵盤焦點、Tab 順序、可讀的 AccessibleName；主控制為圖示按鈕，名稱會跟隨「播放／停止」變化。Slider 保留原生輸入及無障礙行為。
- 自訂標題列可拖曳，提供主題、最小化、退出按鈕；保留 native minimize/system-menu styles。

## 行為

- 啟動不自動播放；點選／鍵盤改選電台立即釋放舊來源並連接新台，按 Enter／Space 或播放圖示亦可開播。點擊停止狀態下已選取的電台，也能重新開播。
- 選取與實際播放仍分開追蹤；新台必須等核心 Playing 事件才標為播放中，不把選取或按下按鈕當作播放成功。
- 選取等於播放台時主控制是停止；選取另一台時變成播放，允許明確切換。系統匣「停止」始終停止實際直播，不受目前選取影響。
- Connecting、Buffering、Playing、Stopped、Error 依播放核心狀態呈現；未開始時為 Idle。失敗保留重試入口，詳細可讀原因放在狀態 Tooltip。
- 停止真正釋放來源，再播放回到當下直播；沒有暫停／時移。每次播放有 40 秒開啟期限，舊解析結果不能開啟新連線。
- 音量與靜音各自設定在既有播放器，不改 Windows 全域音量；靜音不把音量值改成零。滑桿點擊立即跳到對應值、拖曳時即時更新，修正自繪軌道與原生 thumb 命中區不一致的問題，保留原生鍵盤／滾輪／無障礙語意。
- 主題與音量保存於 portable 同資料夾的 `radio.settings.json`。滑鼠操作完成、按鍵放開、滾輪處理後與退出時保存，拖曳中不逐點寫檔。舊版只有主題的設定仍可讀取，切主題不會清掉音量。唯讀資料夾仍可當次調整，不寫 registry／AppData。

## Metadata

- 來源與抽樣證據見 [song-metadata.md](song-metadata.md)。KISS 保留「即將播放」，中廣為「節目／DJ」，Hit FM 僅使用 DJ 欄位；好事 989 依官方節目表及台灣時間顯示節目／已提供的 DJ，明確標為「節目表」而非即時 on-air。其餘無已驗證來源，不猜歌名。
- 可從系統匣的「顯示節目／歌曲資訊」停用；預設開啟。不另開音訊串流，不執行網站 JavaScript。
- 只對實際播放中的受支援電台查詢，至多每 60 秒更新；每次 HTTP 最多 10 秒、256 KiB。資料依 TTL 過期清除；KISS 還檢查回傳時鐘是否在台灣時間前後五分鐘內。
- 缺欄位只顯示有效欄位；沒有資料就隱藏文字，但仍保留固定格位，按鈕、電台名、頻率不會跳動。
- 長文字省略，Tooltip 可看完整內容。HTTP 拒絕／格式變更／逾時只隱藏附加資料，不影響音訊。
- 切台、停止、最小化會取消查詢並清資料；即使舊回應忽略取消，也會因 request identity／station identity 不符而被丟棄。還原即時重新查詢。
- 好事 989 節目表在記憶體快取最多 15 分鐘；每次計算仍使用當下台灣時間，週一至五／週六／週日分欄，跨到下一時段即失效。不從 Facebook 連結猜主持人，不把宣傳文字當 DJ；無資料或表格變更就留白。節目表是官方 HTTP 頁，並非加密或即時播出確認；代班／特殊節目可能不同。

## 系統匣與效能

- 最小化隱藏主視窗，但不停止音訊；停止 metadata loop 與非必要 presentation updates。
- 選單有開啟播放器、播放／停止、metadata 開關與退出；雙擊圖示還原。X 真正退出，取消網路並釋放音訊／系統匣／字型。
- 不以清空工作集或強制 GC 美化數字。縮小後 UI／音訊物件仍存在，不承諾 RAM 會突然大幅下降。
- Media Engine 的 `BUFFERINGSTARTED (1005)` 是來源預先緩衝，不能冒充 playback stall。既有 adapter 現只把 `WAITING (12)` 回報為緩衝中、`PLAYING (13)` 回報為播放中。引擎與來源路由未更換。

## 驗證方式

```powershell
dotnet run --project tests/Radio.Core.Tests -c Release
dotnet run --project tests/Radio.Windows.Tests -c Release -- --ui
dotnet run --project tests/Radio.Windows.Tests -c Release -- --system
```

UI 離線測試注入假播放器／來源／metadata，不連電台、不出聲。包含選取與播放分離、狀態切換、舊回應丟棄、缺欄位、音量／靜音、背景零 presentation update、還原重查、重試、退出及增加清單資料後捲動。

另在新解壓的 Lite 測試目錄做兩次啟動／切主題／退出，確認先保存 `light`，重開後再切換保存 `dark`，驗證實際跨程序載入偏好；不改既有使用者設定。

DPI 測試送出 120／144／192／96 DPI 的 Windows 訊息，檢查界限、字型比例並保留截圖。這是程式排版測試，**不等於真實多螢幕／顯示設定驗收**；仍需人工檢查 100%～200% 下的字型渲染、拖曳、鍵盤及觸控／滑鼠輸入。

實際 portable UI 對照測試（靜音；每階段 10 秒）：

```powershell
Start-Process C:\path\Radio.exe -ArgumentList '--probe --ui-profile --station bcc-ilike --seconds 10 --output C:\path\ui-profile.json' -Wait
```

直接使用該包的 MainForm 與系統引擎，測顯示／最小化／還原的時鐘、CPU、private bytes、working set、presentation update 及 metadata loop 狀態。截圖採真正官方節目內容；離線 UI 截圖則屬測試狀態，不能當成電台內容證據。短測仍不代表人工聽音、長時間、睡眠／斷網恢復已完成。
