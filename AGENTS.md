# Radio Mini — Agent 工作規範

## 專案原則

- 這是既有 .NET 8 WinForms 播放器；沿用 Radio.Core、Radio.Windows 與既有 UI／音訊／metadata，不因局部需求重寫播放器。
- 保留深色極簡風格、字級與已確認的 UI；不得虛構電台節目、DJ、歌曲或播放統計。
- 保留使用者工作與 portable 設定；不提交 radio.settings.json、憑證、bin／obj、dist／artifacts 或測試截圖。
- 使用繁體中文回報。先檢查 git status，不覆蓋／撤銷非自己產生的修改。

## 必須遵循的交付流程

使用者要求的程式、修正、文件或建置變更，預設都走：

**Issue → 功能分支 → 實作與測試 → PR → 使用者確認合併 → Release。**

1. 修改前先查看現有 Issue／PR，重用符合本次需求的未完成紀錄；沒有就建立 Issue，記錄目標、範圍、驗收及限制，回報連結。純問答不必建立 Issue。
2. 從合適且已確認的基底建立分支，例如 feat/42-station-management、fix/43-playback 或 chore/44-workflow。不得直接在 main／master 交付。
3. 實作、文件與測試要一致。提交前檢查 diff、暫存檔與測試結果，不將未執行或失敗的測試說成通過。
4. 使用者說「上 Git／提交／推送」時，意指提交至功能分支、推送並建立 PR，**不是直接推送 main，也不是授權合併**。
5. PR 指向 main，正文用 Closes #<Issue 編號> 連結對應 Issue，列出使用者可見變更、測試及未完成事項；依 .github/pull_request_template.md 填寫。
6. PR 必須有 Release Notes 分類標籤：enhancement（新功能）、bug（修正）、maintenance（重構／建置／流程）或 documentation（文件）。盡量選一個主要分類，方便 .github/release.yml 歸類。
7. 建立 PR 後回報 Issue／PR URL 與驗證結果，等待使用者確認。**不得自行合併、關閉尚未交付的 Issue、刪除分支或建立 tag／發布 Release**；需要另外明確授權。以 PR 合併時的 closing keyword 關閉 Issue。
8. 即使工作很小也保留追蹤；只有使用者明確指定本次例外時才可跳過 Issue／PR／直接推 main，並在回報及追蹤紀錄註明。不可將先前例外當作後續常態。
9. GitHub、權限或網路不可用時保留本機工作並清楚回報，不得偷偷降級為直接推 main，也不得聲稱 Issue／PR 已建立。

歷史補登是例外：既有提交已在 main，可以建立標示 historical 的 Issue、連結原 commit 並以 completed 關閉；不重寫歷史、不建立假 PR、不虛構原始開發／驗收時間。索引見 docs/history-issues.md。

## 驗證

離線 console 測試使用 dotnet run，不是 dotnet test：

```powershell
dotnet build src/Radio.Windows -c Release
dotnet run --project tests/Radio.Core.Tests -c Release
dotnet run --project tests/Radio.Windows.Tests -c Release -- --ui
dotnet run --project tests/Radio.Windows.Tests -c Release -- --system
git diff --check
```

- 按變更範圍選擇測試；純文件／GitHub 範本變更可不重跑音訊測試，但要說明原因並檢查設定與連結。
- 不擅自修改 PowerShell 全機執行政策。線上串流與實體多螢幕驗收不得以離線 fixture 測試替代。
- Release 另需明確授權；依 Release.cmd 與 docs/portable.md 打包，不為一般開發自動產生發行包。

完整操作與 Release Notes 規則見 docs/development-workflow.md。
