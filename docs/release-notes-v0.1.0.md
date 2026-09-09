# GPT Auto Resume v0.1.0-alpha

This is the first public alpha release of GPT Auto Resume, a Windows portable helper for validating safe ChatGPT / Codex Desktop active Work recovery after usage interruptions.

## Alpha Status

This release is for real-world validation.

```yaml
Real Active Work E2E: NOT YET VERIFIED
Multi-Work: NOT SUPPORTED IN v0.1
Default real submission: LOCKED BY DRY RUN
```

## Highlights

- Active Work Auto Resume scope for the currently open ChatGPT / Codex Work.
- Account quota reading for short-window and weekly allowance.
- Traditional Chinese, English, and Japanese UI.
- Localized default resume messages:
  - `請繼續`
  - `Please continue`
  - `続けてください`
- Dry Run safety mode enabled by default.
- Duplicate-send protection.
- Foreground window revalidation before input.
- Composer/input verification.
- Generating-state and incomplete-state gates.
- Optional EasyLifeHub cat artwork banner.
- Windows x64 portable ZIP.

## Important Limits

- v0.1 does not auto-switch between sidebar Works.
- Sidebar titles are not used as production identity.
- ChatGPT Desktop currently does not expose a stable public Work id or conversation deep link for safe multi-Work navigation.
- Real-world automatic input + Enter + generation restart still needs a live validation pass.

## Privacy and Safety

GPT Auto Resume runs locally and does not ask for ChatGPT cookies, bearer tokens, passwords, payment data, or API keys. It avoids fixed-coordinate clicking and OCR action targeting. If a target Work cannot be safely verified, the app aborts instead of typing.
