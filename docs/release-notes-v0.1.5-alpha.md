# 免費幫 ChatGPT / Codex 自動按繼續 v0.1.5-alpha

這是一個免費 Windows 小工具。

你把 ChatGPT / Codex 的 Work 開著，額度用完時它會等；額度恢復後，如果目前開著的 Work 已被允許，而且畫面看起來是被額度中斷，它會幫你輸入 `請繼續` 並按 Enter。

## 這版修了什麼

- 修正「明明額度已恢復，卻一直紅字需要確認」的情況。
- 修正 ChatGPT UIA 資訊變動後，已允許的目前 Work 可能變成未允許的問題。
- 修正額度中斷畫面上殘留 `停止` 控制時，小工具誤以為 AI 還在跑而不按繼續的問題。
- 修正 portable 版診斷工具缺少 pattern 檔時會崩潰的問題。

## 很重要

v0.1 只處理「目前 ChatGPT / Codex 裡開著的那一個 Work」。

它不會自己去 sidebar 巡邏很多 Work，因為 ChatGPT Desktop 目前沒有提供能安全確認每個 sidebar Work 的穩定 ID。這是為了避免把 `請繼續` 送到錯的對話。

## 驗證

- Tests: 225 passed / 0 failed
- Windows x64 portable build

