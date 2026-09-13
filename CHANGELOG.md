# Changelog

## 0.1.4-alpha - 2026-09-13

Wording update for clearer public positioning.

### Changed

- Chinese app card title now says `自動按繼續`.
- GitHub README title now emphasizes the tool is free and helps auto-continue.
- Release notes now use a plain-language `免費自動按繼續小工具` title.

## 0.1.3-alpha - 2026-09-13

Default behavior update for real users.

### Changed

- New installs now default to real Active Work auto resume: `DryRun=false`, `SendEnter=true`, `AllowRealSubmit=true`, and `AutomaticForegroundResume`.
- Existing local config files are still respected, so upgrading users keep their previous choices.
- README now explains in plain language that the app is free and that new installs can type the resume message and press Enter by default.

### Validation

- Added a regression test that locks the new-install defaults.

## 0.1.2-alpha - 2026-09-13

Active Work readiness/status correction after the live-submit fix.

### Fixed

- Non-active remembered Works no longer force the main window into the red `Needs attention` state in the v0.1 Active Work scope.
- Old unconfirmed `Sent=false` resume claims now expire after a short safety lease, so a failed verification does not block the same active Work forever after the sender is fixed.
- Successful `Sent=true` records still block duplicate sends permanently for the same interruption event.

### Validation

- Tests: 220 passed / 0 failed.
- Release build: PASS.
- Active Work live-submit validation from 2026-09-13 remains the latest successful real input + Enter run.

### Still Limited

- Multi-Work sidebar navigation remains unsupported in v0.1.
- Public builds still default to Dry Run safety mode.

## 0.1.1-alpha - 2026-09-13

Active Work live-submit recovery fix.

### Fixed

- Promotes trusted structural ChatGPT / Codex usage-interruption surfaces for the currently active, selected Work when the final action footer is not exposed by UI Automation.
- Avoids repeating heavy completion scans inside the final sender callback after a current-tick trusted validation has already passed.
- Treats ChatGPT ProseMirror placeholder text as an empty composer instead of a user draft.
- Adds a guarded clipboard-paste fallback when UI Automation `ValuePattern` returns but does not actually insert text into the composer.
- Allows an exact pre-existing authorized resume draft, such as `請繼續`, to be submitted instead of blocking forever after an earlier unconfirmed insertion.

### Validation

- Live Active Work test on 2026-09-13 reached input readback PASS, Enter sent, generation restarted, and duplicate journal `Sent=true`.
- Tests: 218 passed / 0 failed.
- Release publish: PASS.

### Still Limited

- Multi-Work sidebar navigation remains unsupported in v0.1.
- Public builds still default to Dry Run safety mode.

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
