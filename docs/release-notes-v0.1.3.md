# GPT Auto Resume v0.1.3-alpha

免費 Windows 小工具。

如果你的 ChatGPT / Codex 跑到一半跳出「額度用完」，你不用一直守在電腦前等重置。

GPT Auto Resume 會幫你盯著目前開著的 Work。等額度恢復後，它會自動輸入：

```text
請繼續
```

然後幫你按 Enter，讓 AI 接著工作。

## 這版最重要的改動

以前下載後預設比較保守，只會偵測，不一定會真的送出。

這版改成比較符合一般人期待：

```text
新安裝後，預設就會真的輸入並送出。
```

也就是說，不需要看完一堆設定說明才知道要開哪個開關。

## 怎麼用

1. 下載 ZIP。
2. 解壓縮後執行 `GPTAutoResume.exe`。
3. 打開 ChatGPT / Codex Desktop。
4. 切到你要它幫忙續跑的 Work。
5. 在小工具裡允許這個 Work 自動續跑。
6. 放著即可。

## 它什麼時候會動

它不會亂送訊息。

必須同時符合：

- 目前開著的 Work 已被你允許
- ChatGPT / Codex 額度已恢復
- AI 已經停住，不是正在跑
- 看起來不像正常完成
- 輸入框可以安全使用
- 同一次中斷還沒送過

符合後才會輸入續跑訊息並按 Enter。

## 先講清楚限制

v0.1 只支援「目前開著的那一個 Work」。

它不會自己去 sidebar 裡面巡邏很多個 Work。因為目前 ChatGPT Desktop 沒有提供穩定的 Work ID，不能 100% 確認 sidebar 裡哪個項目就是原本那個對話。只靠標題很危險，送錯地方比不送更糟。

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
實際啟動設定檢查：PASS
測試：221 passed / 0 failed
GitHub Actions：PASS
```
