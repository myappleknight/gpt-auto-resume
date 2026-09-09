# Contributing

Thanks for helping improve GPT Auto Resume.

## Good first contributions

- Add real usage-limit wording to `src/GPTAutoResume/Resources/usage_limit_patterns.json`
- Add retry-time parsing examples as tests
- Improve UI Automation discovery notes for ChatGPT / Codex desktop versions
- Add screenshots for fresh Windows runs

## Rules

- Keep detection conservative
- Do not add telemetry
- Do not store full conversations
- Do not add account login, license keys, or paid gating
- Do not scrape cookies or bearer tokens
- Do not use sidebar titles as production identity
- Keep unknown target states fail-closed
- Add tests for detector or time-parser changes

## Development

```powershell
dotnet restore
dotnet test
```
