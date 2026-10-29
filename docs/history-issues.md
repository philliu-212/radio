# 歷史提交與 Issue 索引

以下紀錄於 **2026-10-29 事後補登**，涵蓋建立新流程前 main 上的全部 12 筆提交。每張 Issue 皆標示 historical，連結原始 commit、摘要與可確認的相關 PR，並以 completed 關閉。

建立／關閉 Issue 的時間不是原始實作時間；補登不代表重跑歷史版本、不會重寫 Git 歷史，也不會補造 PR 或 Release。補登工作由 [#4](https://github.com/philliu-212/radio/issues/4) 追蹤。

| Commit | 原始提交日期 | 已交付內容 | 補登 Issue | 既有 PR |
| --- | --- | --- | --- | --- |
| [192005c](https://github.com/philliu-212/radio/commit/192005c1d18ab925a75fe00e01d25731925e7b00) | 2026-10-27 | 初始 Windows 播放器、官方來源與原生／當時的 mpv 後端 | [#5](https://github.com/philliu-212/radio/issues/5) | 無；為 #1 的既有基底，不在該 PR 提交範圍 |
| [1d3d415](https://github.com/philliu-212/radio/commit/1d3d4151ac68e10cab4d6e15046b8dc60e06a3b6) | 2026-10-27 | 系統純音訊與 Lite／Full portable | [#6](https://github.com/philliu-212/radio/issues/6) | [#1](https://github.com/philliu-212/radio/pull/1)，已合併 |
| [9eb4454](https://github.com/philliu-212/radio/commit/9eb4454ff8f1f10ff1af4dc411354909eababf39) | 2026-10-28 | Direction C UI、主題與官方 metadata | [#7](https://github.com/philliu-212/radio/issues/7) | #1，已合併 |
| [d04893d](https://github.com/philliu-212/radio/commit/d04893dd51241e1b1ddd523c21ef2c592a5b3894) | 2026-10-28 | dist 只保留當前 portable 產物的文件 | [#8](https://github.com/philliu-212/radio/issues/8) | #1，已合併，merge commit 指向本提交 |
| [178a822](https://github.com/philliu-212/radio/commit/178a822a57af50da5f97b9c1ca5333bd3ce94349) | 2026-10-28 | 音量保存、緊湊清單及好事989節目表 | [#9](https://github.com/philliu-212/radio/issues/9) | [#2](https://github.com/philliu-212/radio/pull/2)，已關閉、未合併 |
| [b599977](https://github.com/philliu-212/radio/commit/b59997750f09aca9040779041c459030e814dc8a) | 2026-10-28 | 捲動條 hover 與圓角列內縮 | [#10](https://github.com/philliu-212/radio/issues/10) | [#3](https://github.com/philliu-212/radio/pull/3)，已關閉、未合併 |
| [149273a](https://github.com/philliu-212/radio/commit/149273a7c2aef9be7656725c224f8f781ee574d0) | 2026-10-28 | v0.3.3 版面與一鍵 portable 建置 | [#11](https://github.com/philliu-212/radio/issues/11) | 無 |
| [d691dbd](https://github.com/philliu-212/radio/commit/d691dbdb8d515244c8c1e115ae945c063f4fe1de) | 2026-10-28 | 電台清單 hover 閃爍修正（v0.3.4） | [#12](https://github.com/philliu-212/radio/issues/12) | 無 |
| [58b4b5d](https://github.com/philliu-212/radio/commit/58b4b5ddf438f0df18e3dea365665ee911a37a2e) | 2026-10-28 | 捲動邊界重繪與工作列最小化還原 | [#13](https://github.com/philliu-212/radio/issues/13) | 無 |
| [1054d59](https://github.com/philliu-212/radio/commit/1054d59c7bf569e11fdcdb0de9f69c98cec24348) | 2026-10-28 | 統一建置、版本顯示及移除 mpv | [#14](https://github.com/philliu-212/radio/issues/14) | 無 |
| [93f38af](https://github.com/philliu-212/radio/commit/93f38af88bec6d6f869be495f2f591de54e4cdab) | 2026-10-28 | RFI Monde 法語網路電台 | [#15](https://github.com/philliu-212/radio/issues/15) | 無 |
| [44b93a0](https://github.com/philliu-212/radio/commit/44b93a0dbd51daa254410253c2b11a359b6a9e19) | 2026-10-29 | 電台管理、搜尋、多選、四顆箭頭及持久化播放統計 | [#16](https://github.com/philliu-212/radio/issues/16) | 無；新流程建立前直接推送 main |

## 用於 Release Notes

- 這張表與 Issue 用來追蹤早期直接提交的變更；版本範圍須以實際 commit／tag 比對，不能把所有補登紀錄當作同一次新版功能。
- GitHub 自動產生 Release Notes 主要彙整版本範圍內**已合併 PR**，不會因補建 Issue 就自動補出缺少的歷史 PR。
- 初次整理歷史 Release Notes 時，先確認目標 commit 與前一版本範圍，再從本表選擇正確項目，手動補入其 Issue／commit 連結。
- 後續走 [開發流程](development-workflow.md)，以 PR 與分類標籤產生可追蹤的發佈內容。
