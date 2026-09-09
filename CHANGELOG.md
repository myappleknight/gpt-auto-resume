# Changelog

## 0.1.0-alpha - 2026-09-10

Initial public alpha for real-world validation.

### Added

- Active Work Auto Resume scope for the currently open ChatGPT / Codex Desktop Work.
- Account quota reading for short-window and weekly allowance state.
- Localized default resume messages for Traditional Chinese, English, and Japanese.
- Work permission selection for the currently observed active Work.
- Dry Run mode enabled by default.
- Duplicate-send protection and local event journal.
- Foreground HWND revalidation before input.
- Composer/input candidate verification.
- Generation-running protection.
- Incomplete / continue-state detection gates.
- Local-only settings.
- Optional EasyLifeHub cat banner with local cache and OFF switch.
- Windows x64 portable publish flow.
- GitHub Actions CI and release workflow.

### Security

- No fixed-coordinate clicking.
- No OCR action targeting.
- No cookie scraping.
- No bearer-token scraping.
- No sidebar-title identity for production resume.
- Fail-closed behavior when the active Work cannot be safely verified.

### Known Limitations

- Real Active Work automatic resume E2E is not yet verified.
- Multi-Work auto navigation is not supported in v0.1.
- ChatGPT Desktop currently does not expose a stable public Work id or deep link for safe non-active Work switching.
- The alpha build defaults to Dry Run safety mode.
