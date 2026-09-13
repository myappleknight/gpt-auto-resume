# GPT Auto Resume

Keep your active ChatGPT / Codex Desktop work moving after usage interruptions.

Chinchilla Design Lab presents. 金吉拉低賽製作所. Built by EasyLifeHub.

> Alpha status: `v0.1.2-alpha` is a public validation release. Active Work automatic resume has been live-tested once against ChatGPT / Codex Desktop; multi-Work navigation is not supported in v0.1.

[繁體中文 README](README.zh-TW.md)

## What It Does

GPT Auto Resume is a small Windows portable helper for ChatGPT / Codex Desktop. It watches the currently open Work, detects usage interruption and quota recovery signals, then safely prepares a localized resume message such as `Please continue`.

The alpha build is intentionally conservative. By default it runs in safe Dry Run mode so users can validate detection before enabling real submission.

## Who It Is For

- Codex users who leave long-running active Work open.
- Builders who want a local helper for usage-interruption recovery.
- Testers who can help validate real ChatGPT / Codex Desktop wording and UI states.

## Feature Status

| Feature | v0.1.2-alpha |
| --- | --- |
| Active Work monitoring | Yes |
| Account quota reading | Yes |
| Localized resume message | Yes |
| Traditional Chinese UI | Yes |
| English UI | Yes |
| Japanese UI | Yes |
| Dry Run safety mode | Yes, default |
| Duplicate-send protection | Yes |
| Foreground window revalidation | Yes |
| Composer verification | Yes |
| Fixed-coordinate clicking | No |
| OCR action targeting | No |
| Cookie/token scraping | No |
| Multi-Work navigation | Not supported |
| Sidebar-title auto switching | Not supported |
| Real Active Work E2E | Verified once |

## v0.1.2-alpha Update

This update fixes the final live-submit path for active Work recovery and a red `Needs attention` state that could appear even when the active Work was safe:

- Trusted structural usage-interruption surfaces can qualify an active selected Work when the final footer is not exposed by UI Automation.
- ChatGPT ProseMirror placeholders are no longer mistaken for user drafts.
- If UI Automation `ValuePattern` accepts input but does not actually write into the composer, the app uses a guarded clipboard-paste fallback after revalidating the foreground window, active Work identity, quota, and empty composer.
- If the configured resume message is already present from a prior failed verification, the app can submit that exact authorized draft instead of blocking forever.
- Non-active remembered Works no longer force the main status into a red confirmation state in v0.1 Active Work scope.
- Old unconfirmed failed claims expire, while successful `Sent=true` records still block duplicate sends.

Live validation on 2026-09-13:

```yaml
Active Work: selected and revalidated
Quota: available
Input readback: PASS
Enter: sent
Generation restarted: PASS
Duplicate journal: Sent=true
```

## v0.1 Scope: Active Work Only

GPT Auto Resume v0.1 only works with the ChatGPT / Codex Work that is currently open and explicitly permitted in the app.

It does not patrol every checked sidebar item. ChatGPT Desktop does not currently expose a stable public Work id, conversation id, or deep link that can safely identify and reopen non-active sidebar Works. Sidebar titles are display labels only; they are not used as production identity.

## Safety Model

Auto resume is fail-closed. If the app cannot prove the target is safe, it does nothing.

Key protections:

- Uses account quota data as the quota source, not old conversation text.
- Limits v0.1 automation to the current active Work.
- Requires explicit Work permission.
- Checks that generation is not still running.
- Uses completion / incomplete-state evidence before resume.
- Waits for repeated stable observations before action.
- Revalidates foreground window handle before input.
- Verifies the composer/input candidate.
- Records duplicate-send claims before input.
- Aborts if target identity cannot be revalidated.
- Does not use fixed coordinates, OCR targeting, cookies, bearer tokens, or private web endpoints.

## Default Resume Messages

| Language | Default message |
| --- | --- |
| Traditional Chinese | `請繼續` |
| English | `Please continue` |
| Japanese | `続けてください` |

Users can set a custom resume message. Custom text is stored locally.

## Install

Download the Windows x64 portable ZIP from the GitHub Release page:

```text
GPT-Auto-Resume-v0.1.2-alpha-win-x64.zip
```

Unzip it anywhere and run:

```text
GPTAutoResume.exe
```

No installer is required.

## System Requirements

- Windows 10 or Windows 11
- ChatGPT / Codex Desktop
- Windows x64

## Privacy

GPT Auto Resume runs locally. It does not ask for your ChatGPT password, cookies, bearer token, payment data, or API keys.

The app reads limited local UI metadata and account quota state needed for detection and safety checks. It should not save full private conversations. Diagnostic files are local and should be reviewed before sharing.

The optional cat banner downloads artwork from EasyLifeHub and caches it locally. Turning the banner off prevents banner network requests. Clicking the banner opens the 金吉拉低賽 Webtoons page in your default browser; the app never opens it automatically.

## Troubleshooting

### The app says it needs confirmation

The active Work may not be safely identified, may still be running, or may not be permitted. Open the Work you want to monitor and allow it in the app.

### It did not type anything

In `v0.1.2-alpha`, Dry Run is enabled by default. This is intentional so real users can validate detection before enabling real submission.

### My checked sidebar Work did not resume

v0.1 does not support multi-Work sidebar navigation. Only the currently open active Work is in scope.

### Quota looks stale

Manual quota refresh reads account quota again. Automatic background checks are deliberately lightweight so the app does not disturb normal work.

## Development

Install .NET 8 SDK on Windows:

```powershell
dotnet restore
dotnet test
dotnet publish src/GPTAutoResume/GPTAutoResume.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/win-x64
```

## Roadmap

v0.1.x:

- Real-world Active Work E2E validation.
- More usage-interruption fixtures.
- Safer completion-state detection.
- UX polish based on real alpha feedback.

Future, dependent on ChatGPT Desktop capabilities:

- Stable Desktop Work identity.
- Safe multi-Work navigation.
- Multiple selected Works auto-resume.

## License

MIT License. See [LICENSE](LICENSE).
