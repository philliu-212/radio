# Issue → PR → Release 開發流程

從歷史補登工作 [#4](https://github.com/philliu-212/radio/issues/4) 起，所有程式、修正、文件、建置與流程維護預設走此流程。根目錄 [AGENTS.md](../AGENTS.md) 是後續 agent 的專案指引，不依賴某次聊天記憶。

## 1. 先記錄 Issue

- 先搜尋現有 Issue／PR，避免重複。
- Agent 自行依工作關聯性決定 Issue 數量，不必請使用者分單。不相關的功能、bug 或流程需求必須各自建立／重用 Issue，即使來自同一則訊息；每張保持可獨立理解、驗收及追蹤。相關項目若共用實作或回歸測試，可交叉連結，但不得用單一大 Issue 混掉不同需求。
- PR 正文需逐項對照所涵蓋的 Issue 與測試；不相關的流程／文件維護與產品功能使用不同 PR，避免後續 agent 無法辨識交付範圍。
- 每個需求寫清目標、範圍、驗收條件與限制；bug 附重現方式。
- 以 `.github/ISSUE_TEMPLATE/work-item.md` 建立追蹤；純討論不必開 Issue。

## 2. 在功能分支實作

從適當基底建立 `feat/<編號>-<摘要>`、`fix/<編號>-<摘要>` 或 `chore/<編號>-<摘要>`。開始前檢查工作目錄，不覆蓋使用者修改。不可因工作很小就直接推 main。

按需求補程式、測試與文件；提交前檢查 diff。播放器核心／UI／原生音訊的驗證指令見 AGENTS.md。純文件／GitHub 範本修改可略過音訊測試，但必須在 PR 說明並驗證設定、連結及 Git diff。

## 3. 推送功能分支並建立 PR

使用者說「上 Git」時，預設授權提交、推送**功能分支**與建立 PR，不包含合併或發行。

PR 必須：

- 以 main 為基底，依 PR template 填寫摘要、測試、Release Notes 與限制。
- 正文包含 `Closes #<Issue 編號>`，讓合併自動關閉需求；不要提早關閉未交付的 Issue。
- 選擇一個主要分類標籤：`enhancement`、`bug`、`maintenance` 或 `documentation`。
- 回報 PR URL，等待使用者確認。未授權不得自行合併、建立 tag 或發布 Release；合併後刪除該功能分支屬流程的一部分。

GitHub 或權限不可用時保留本機工作，明確說明阻擋；不能跳過追蹤或聲稱遠端操作已完成。

## 4. 確認後合併

由使用者審查，或在使用者明確授權後合併。保留 PR 連結、Issue 關聯與測試紀錄。這些規範不是 GitHub 分支保護設定；本次未修改倉庫規則或既有權限。

合併後立即刪除對應功能分支的遠端與本機分支（`git push origin --delete <branch>`、`git branch -d <branch>`）。分支僅為交付載體，紀錄留在 PR；仍有 worktree 使用或未合併內容時先處理，不強制刪除。

## 5. 產生 Release Notes

只有另行收到發行授權時才建立版本／tag／Release 或執行封裝。`.github/release.yml` 按已合併 PR 的標籤分類為：

- 新功能：enhancement
- 問題修正：bug
- 重構、建置與流程維護：maintenance
- 文件：documentation
- 其他變更：未分類項目

GitHub 的 Generate release notes 主要使用指定版本範圍的已合併 PR。PR 正文的 Release Notes 段落供人工潤飾使用，不代表 GitHub 會逐字自動匯入。發布前仍要確認版本範圍、內容、驗證限制與 portable 產物；不要把文件維護誤寫成播放器新功能。

歷史直接提交沒有對應 PR 的部分，依 [歷史索引](history-issues.md) 和 commit／tag 範圍手動補入；不建立假 PR、不重寫歷史。

## 歷史補登與例外

歷史補登 Issue 標示 `historical`，明確連結已存在 main 的原始 commit，可直接以 completed 關閉。此例外只適用既有工作補登，不適用日後的新需求。

使用者若明確要求單次跳過流程，須在追蹤紀錄與回報註明原因；不得將單次例外推廣為所有後續工作。
