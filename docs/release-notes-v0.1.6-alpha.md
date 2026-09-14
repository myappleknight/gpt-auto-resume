# 免費幫 ChatGPT / Codex 自動按繼續 v0.1.6-alpha

這是一個免費 Windows 小工具。

你把 ChatGPT / Codex 的工作開著。額度用完時，它等；額度恢復後，它幫你輸入 `請繼續`，再按 Enter。

## 這版修了什麼

- 你在有額度時才勾選目前工作，現在會立刻檢查，不用再自己按確認。
- 程式輸入並送出 `請繼續` 後，不會馬上又變紅字「需要確認」。
- 送出後如果 ChatGPT / Codex 還在準備回應，畫面會保持在驗證/等待狀態。

## 免費

這個工具免費使用。

## 很重要

v0.1 只處理「目前 ChatGPT / Codex 裡開著的那一個工作」。

它不會自己去 sidebar 巡邏很多工作，因為 ChatGPT Desktop 目前沒有提供能安全確認每個 sidebar 工作的穩定 ID。這是為了避免把 `請繼續` 送到錯的對話。

## 驗證

- Tests: 227 passed / 0 failed
- Windows x64 portable build
