# 免費幫 ChatGPT / Codex 自動按繼續

GPT Auto Resume 是免費 Windows 小工具。ChatGPT / Codex 跑到一半被額度卡住時，它會幫你盯著。

額度恢復後，它會回到你目前開著、也已經允許的小工作，輸入 `請繼續`，再按 Enter，讓 AI 接著做下去。

金吉拉低賽製作所。EasyLifeHub 製作。

[English README](README.md)

## 一句話看懂

你不用付費買這個工具，也不用一直守在電腦前等額度恢復。

GPT Auto Resume 會幫你看：

- 現在 ChatGPT / Codex 還有沒有額度
- 目前開著的 Work 有沒有被你允許自動按繼續
- AI 是不是已經停住，而且看起來還沒完成
- 條件都對時，自動輸入續跑訊息並按 Enter

## 它適合誰

- 會讓 Codex 跑很久的人
- 常常遇到「額度用完，等重置後要手動打請繼續」的人
- 不想半夜或出門時一直盯著 ChatGPT 的人

## 它現在能做什麼

| 功能 | v0.1.6-alpha |
| --- | --- |
| 免費使用 | 是 |
| 監控目前開著的 ChatGPT / Codex Work | 可以 |
| 讀取帳號剩餘額度 | 可以 |
| 額度恢復後自動輸入 `請繼續` | 可以，需先允許該 Work |
| 自動按 Enter | 可以，新安裝預設開啟 |
| 防止同一次中斷重複送出 | 可以 |
| 繁體中文 / English / 日本語介面 | 可以 |
| 一次巡邏 sidebar 裡很多 Work | v0.1 不支援 |
| 靠 sidebar 標題猜是哪個 Work | 不做，避免送錯 |

## 先講清楚限制

v0.1 只處理「目前 ChatGPT / Codex 視窗裡開著的那一個 Work」。

它不會自己去 sidebar 裡面一個一個切換你勾選的 Work。原因很簡單：目前 ChatGPT Desktop 沒有公開穩定的 Work ID 或連結，可以讓小工具 100% 確認「這就是原本那個對話」。只靠標題很危險，兩個 Work 可能同名，送錯地方比不送更糟。

所以現在的正確用法是：

```text
打開你要續跑的 Work
↓
在 GPT Auto Resume 裡允許它
↓
讓它保持開著
↓
額度恢復後，小工具才會幫這個 Work 續跑
```

## 怎麼使用

1. 到 GitHub Release 下載：

```text
GPT-Auto-Resume-v0.1.6-alpha-win-x64.zip
```

2. 解壓縮。

3. 執行：

```text
GPTAutoResume.exe
```

4. 打開 ChatGPT / Codex Desktop。

5. 切到你要續跑的 Work。

6. 在小工具裡確認這個 Work 已被允許自動按繼續。

7. 放著即可。

第一次安裝的新使用者，預設就是會真送出：會輸入續跑訊息，也會按 Enter。若你之前已經開過舊版，程式會沿用你原本的本機設定；需要時可到設定頁確認 Dry Run 是否關閉、Enter 是否開啟。

## 什麼時候會自動送出

它不會看到額度有了就亂送。

必須同時符合：

- 目前開著的是已允許的 Work
- 帳號額度已恢復
- AI 不是正在生成中
- 最後狀態看起來是被中斷、還沒正常完成
- 小工具連續確認狀態穩定
- 輸入框是安全可用的
- 沒有同一次中斷已經送過的紀錄

符合後才會輸入續跑訊息，例如：

```text
請繼續
```

然後按 Enter。

## 為什麼有時候它不動

常見原因：

- 目前開著的 Work 還在跑
- 目前開著的 Work 沒有被允許
- 這個回覆看起來已經正常完成
- 小工具無法安全確認目前 Work
- 你勾的是 sidebar 裡別的 Work，但那個 Work 不是目前開著的 Work
- 舊版留下的本機設定仍是 Dry Run，只做偵測不真送

這些情況它會選擇不動。這是刻意設計，避免把 `請繼續` 送到錯的地方。

## 預設續跑訊息

| 介面語言 | 預設訊息 |
| --- | --- |
| 繁體中文 | `請繼續` |
| English | `Please continue` |
| 日本語 | `続けてください` |

你可以改成自己的文字，自訂內容只存在你的電腦裡。

## 隱私與安全

GPT Auto Resume 在你的電腦本機執行。

它不會要求：

- ChatGPT 密碼
- cookie
- bearer token
- API key
- 付款資料

它也不靠固定座標亂點、不用 OCR 猜畫面、不用 sidebar 標題當真正身份。

## 下載

最新版：

[GPT Auto Resume v0.1.6-alpha](https://github.com/myappleknight/gpt-auto-resume/releases/tag/v0.1.6-alpha)

Windows x64 ZIP：

[GPT-Auto-Resume-v0.1.6-alpha-win-x64.zip](https://github.com/myappleknight/gpt-auto-resume/releases/download/v0.1.6-alpha/GPT-Auto-Resume-v0.1.6-alpha-win-x64.zip)

## 給開發者

需要自己 build 時：

```powershell
dotnet restore
dotnet test
dotnet publish src/GPTAutoResume/GPTAutoResume.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/win-x64
```

## 接下來想改善

- 收集更多真實額度中斷案例
- 讓「最後回覆是否完成」判斷更穩
- 等 ChatGPT Desktop 未來若提供穩定 Work ID，再支援多 Work 自動按繼續

## License

MIT License。請見 [LICENSE](LICENSE)。

