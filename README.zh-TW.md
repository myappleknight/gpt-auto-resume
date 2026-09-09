# GPT Auto Resume

讓目前開啟中的 ChatGPT / Codex Work，在使用額度恢復後可以安全接著跑。

金吉拉低賽製作所。Chinchilla Design Lab presents。EasyLifeHub 製作。

> Alpha 狀態：`v0.1.0-alpha` 是公開驗證版。真實「額度中斷 → 額度恢復 → 自動輸入 → Enter → 重新開始」端到端驗證尚未完成；v0.1 也不支援多 Work 自動巡邏。

[English README](README.md)

## 這是什麼

GPT Auto Resume 是一款 Windows 可攜式小工具，用來監控目前開啟中的 ChatGPT / Codex Work。當工作因使用額度中斷，程式會等待額度恢復，重新確認目前工作的安全狀態，再準備送出對應語言的續跑訊息，例如 `請繼續`。

Alpha 版預設採安全 Dry Run 模式，方便先驗證偵測是否可靠，不會一下載就真的幫你送出。

## 適合誰

- 會讓 Codex 長時間跑工作的使用者。
- 想在本機監控額度恢復、減少手動盯畫面的人。
- 願意協助驗證 ChatGPT / Codex Desktop 真實中斷狀態的 alpha 測試者。

## 功能狀態

| 功能 | v0.1.0-alpha |
| --- | --- |
| 目前開啟中的 Work 監控 | 支援 |
| 帳號額度讀取 | 支援 |
| 多語續跑訊息 | 支援 |
| 繁體中文介面 | 支援 |
| English UI | 支援 |
| 日本語 UI | 支援 |
| Dry Run 安全模式 | 預設開啟 |
| 防重複送出 | 支援 |
| 前景視窗重新驗證 | 支援 |
| 聊天輸入框驗證 | 支援 |
| 固定座標點擊 | 不使用 |
| OCR 動作定位 | 不使用 |
| Cookie / token 擷取 | 不使用 |
| 多 Work 自動切換 | v0.1 不支援 |
| 用 sidebar 標題自動切換 | 不支援 |
| 真實 Active Work E2E | 尚未驗證完成 |

## v0.1 範圍：只支援目前開啟中的 Work

GPT Auto Resume v0.1 只處理 ChatGPT / Codex Desktop 目前正在開啟、且你已在小工具中允許的 Work。

它不會自動巡邏 sidebar 裡所有打勾的 Work。原因是目前 ChatGPT Desktop 沒有公開、穩定、可驗證的 Work id、conversation id 或 deep link，可以安全地重新定位未開啟的 Work。Sidebar 標題只能拿來顯示，不能當作送出訊息的身份依據。

## 安全設計

這個工具採 fail-closed：只要無法證明安全，就不動作。

主要保護：

- 額度來源以帳號額度資料為主，不把舊對話文字當額度來源。
- v0.1 僅限目前開啟中的 Active Work。
- Work 必須先被使用者允許。
- 工作仍在生成時不送。
- 需要確認最後狀態是不完整停止，而不是正常完成或未知。
- 需要連續穩定觀察後才進入續跑判斷。
- 輸入前重新驗證前景視窗 HWND。
- 驗證真正的聊天輸入框。
- 輸入前先寫入防重複送出紀錄。
- 無法驗證原 Work 身份時直接中止。
- 不用固定座標、不用 OCR 定位、不擷取 cookie、不擷取 bearer token、不使用私有登入 endpoint。

## 預設續跑訊息

| 語言 | 預設訊息 |
| --- | --- |
| 繁體中文 | `請繼續` |
| English | `Please continue` |
| 日本語 | `続けてください` |

你也可以改成自己的續跑訊息。自訂文字只保存在本機。

## 安裝與使用

從 GitHub Release 下載 Windows x64 portable ZIP：

```text
GPT-Auto-Resume-v0.1.0-alpha-win-x64.zip
```

解壓縮後執行：

```text
GPTAutoResume.exe
```

不需要安裝程式。

## 系統需求

- Windows 10 或 Windows 11
- ChatGPT / Codex Desktop
- Windows x64

## 隱私

GPT Auto Resume 在本機執行。它不會要求你的 ChatGPT 密碼、cookie、bearer token、付款資料或 API key。

程式會讀取有限的本機 UI metadata 與帳號額度狀態，用於偵測與安全確認。它不應保存完整私人對話。若要分享診斷檔，請先自行檢查內容。

可選的貓咪 Banner 會從 EasyLifeHub 下載圖片並存在本機快取。關閉 Banner 後不會請求 Banner server。使用者主動點擊 Banner 時，會用系統預設瀏覽器開啟金吉拉低賽 Webtoons 漫畫頁；程式不會自動開啟。

## 疑難排解

### 畫面顯示「需要確認」

代表目前 Work 可能還在執行、尚未允許，或小工具無法安全確認它就是要續跑的目標。請打開你要監控的 Work，並在小工具中允許它。

### 為什麼沒有真的輸入？

`v0.1.0-alpha` 預設是 Dry Run。這是刻意設計，目的是讓使用者先驗證偵測結果，再決定是否開啟真送出。

### 我勾選的其他 sidebar Work 為什麼沒續跑？

v0.1 不支援多 Work 自動切換。只有目前開啟中的 Active Work 屬於支援範圍。

### 額度看起來沒有即時更新

可以手動重新讀取額度。自動背景檢查會保持輕量，避免打擾正常使用。

## 開發

Windows 安裝 .NET 8 SDK 後：

```powershell
dotnet restore
dotnet test
dotnet publish src/GPTAutoResume/GPTAutoResume.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/win-x64
```

## Roadmap

v0.1.x：

- 真實 Active Work E2E 驗證。
- 增加更多真實中斷文案 fixture。
- 強化最後回覆完成/不完整狀態判斷。
- 依 alpha 回饋微調 UI。

未來，取決於 ChatGPT Desktop 是否提供穩定能力：

- 穩定 Desktop Work identity。
- 安全的多 Work 導航。
- 多個已選 Work 自動續跑。

## License

MIT License。請見 [LICENSE](LICENSE)。
