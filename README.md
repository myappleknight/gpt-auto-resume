# Free Helper That Clicks Continue For ChatGPT / Codex

GPT Auto Resume is a free Windows helper. When ChatGPT / Codex stops because your usage limit is reached, this little app watches for quota recovery.

When quota comes back, it can type `Please continue` into the currently open and allowed Work, press Enter, and let the AI keep going.

Made by EasyLifeHub. Chinchilla Design Lab presents. 金吉拉低賽製作所.

[繁體中文 README](README.zh-TW.md)

## Understand It In One Sentence

You do not have to pay for this tool or sit in front of the computer waiting for your ChatGPT / Codex quota to reset.

GPT Auto Resume watches:

- whether ChatGPT / Codex has quota again
- whether the currently open Work is allowed
- whether the AI has stopped before finishing
- whether it is safe to type the resume message

If everything looks safe, it sends the resume message for you.

## Who This Is For

- People who leave Codex running for long tasks
- People who often hit usage limits and have to come back later
- People who want ChatGPT / Codex to continue after quota resets without babysitting it

## What It Can Do Today

| Feature | v0.1.6-alpha |
| --- | --- |
| Free to use | Yes |
| Watch the currently open ChatGPT / Codex Work | Yes |
| Read account quota | Yes |
| Type `Please continue` after quota returns | Yes, for an allowed active Work |
| Press Enter | Yes, enabled by default for new installs |
| Avoid duplicate sends for the same interruption | Yes |
| Traditional Chinese / English / Japanese UI | Yes |
| Patrol many sidebar Works automatically | Not supported in v0.1 |
| Guess a Work by sidebar title | No, to avoid sending to the wrong place |

## Important Limit

v0.1 only works with the Work that is currently open in ChatGPT / Codex Desktop.

It does not jump around the sidebar and resume every checked Work. ChatGPT Desktop currently does not expose a stable public Work ID or deep link that lets this app prove, with certainty, which sidebar conversation is which. Titles are not enough because two Works can have the same name.

The safe way to use v0.1 is:

```text
Open the Work you want to resume
↓
Allow it in GPT Auto Resume
↓
Keep that Work open
↓
When quota returns, the app can resume that Work
```

## How To Use

1. Download the Windows ZIP from GitHub Releases:

```text
GPT-Auto-Resume-v0.1.6-alpha-win-x64.zip
```

2. Unzip it.

3. Run:

```text
GPTAutoResume.exe
```

4. Open ChatGPT / Codex Desktop.

5. Open the Work you want to continue.

6. Allow that Work inside GPT Auto Resume.

7. Leave it running.

For new installs, real submit is enabled by default: the app can type the resume message and press Enter. If you already used an older version, the app keeps your existing local settings; check Settings if you want to confirm that Dry Run is off and Enter is enabled.

## When It Will Send

The app does not type just because quota is available.

It only sends when all of these are true:

- the currently open Work is allowed
- account quota is available
- the AI is not still generating
- the last state looks interrupted rather than normally finished
- the same state is seen more than once
- the input box is safe to use
- the same interruption has not already been resumed

Then it types a resume message, for example:

```text
Please continue
```

and presses Enter.

## Why It Might Not Do Anything

Common reasons:

- the current Work is still running
- the current Work has not been allowed
- the last response already looks complete
- the app cannot safely verify the current Work
- the Work you checked is in the sidebar, but it is not the Work currently open
- an older local setting still has Dry Run enabled, which detects but does not really submit

When unsure, the app does nothing. That is intentional: not sending is better than sending to the wrong Work.

## Default Resume Messages

| UI language | Default message |
| --- | --- |
| Traditional Chinese | `請繼續` |
| English | `Please continue` |
| Japanese | `続けてください` |

You can set your own message. Custom text stays on your computer.

## Privacy And Safety

GPT Auto Resume runs locally on your PC.

It does not ask for:

- your ChatGPT password
- cookies
- bearer tokens
- API keys
- payment data

It does not use fixed-coordinate clicking, OCR guessing, or sidebar titles as the real Work identity.

## Download

Latest release:

[GPT Auto Resume v0.1.6-alpha](https://github.com/myappleknight/gpt-auto-resume/releases/tag/v0.1.6-alpha)

Windows x64 ZIP:

[GPT-Auto-Resume-v0.1.6-alpha-win-x64.zip](https://github.com/myappleknight/gpt-auto-resume/releases/download/v0.1.6-alpha/GPT-Auto-Resume-v0.1.6-alpha-win-x64.zip)

## For Developers

To build it yourself:

```powershell
dotnet restore
dotnet test
dotnet publish src/GPTAutoResume/GPTAutoResume.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/win-x64
```

## Roadmap

- Collect more real usage-limit interruption cases
- Improve final-response completion detection
- Add multi-Work auto resume only if ChatGPT Desktop exposes a stable Work ID in the future

## License

MIT License. See [LICENSE](LICENSE).

