# 免費幫 ChatGPT / Codex 自動按繼續

GPT Auto Resume v0.1.4-alpha 是免費 Windows 小工具。

如果你的 ChatGPT / Codex 跑到一半跳出「額度用完」，你不用一直守在電腦前等重置。

GPT Auto Resume 會幫你盯著目前開著的 Work。等額度恢復後，它會自動輸入：

```text
請繼續
```

然後幫你按 Enter，讓 AI 接著工作。

## 這版改了什麼

這版把使用者最先看到的標題與介面文案改得更直覺：

```text
自動按繼續
```

不再讓人誤會只是「恢復狀態」或「只做偵測」。它的重點就是：符合安全條件後，幫你按繼續。

## 怎麼用

1. 下載 ZIP。
2. 解壓縮後執行 `GPTAutoResume.exe`。
3. 打開 ChatGPT / Codex Desktop。
4. 切到你要它幫忙續跑的 Work。
5. 在小工具裡允許這個 Work 自動按繼續。
6. 放著即可。

## 先講清楚限制

v0.1 只支援「目前開著的那一個 Work」。

它不會自己去 sidebar 裡面巡邏很多個 Work。原因是目前 ChatGPT Desktop 沒有提供穩定的 Work ID，不能 100% 確認 sidebar 裡哪個項目就是原本那個對話。

## 免費與隱私

- 免費使用
- 不需要安裝
- 不需要 ChatGPT 密碼
- 不讀 cookie
- 不讀 bearer token
- 不需要 API key
- 不靠固定座標亂點
- 不用 OCR 猜畫面

## 驗證狀態

```text
新安裝預設真送出：PASS
自動按繼續文案更新：PASS
測試：221 passed / 0 failed
```
