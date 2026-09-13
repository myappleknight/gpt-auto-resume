# GPT Auto Resume v0.1.1-alpha

This alpha update fixes the Active Work live-submit path after a real ChatGPT / Codex usage interruption.

## Status

```yaml
Active Work real resume: VERIFIED ONCE
Multi-Work navigation: NOT SUPPORTED IN v0.1
Default real submission: LOCKED BY DRY RUN
```

## What Changed

- Trusted structural usage-limit surfaces can now qualify the current selected Active Work when the latest response footer is not exposed by UI Automation.
- The sender no longer repeats expensive completion scans inside the final input callback after a trusted current-tick validation already passed.
- ChatGPT ProseMirror placeholder text is treated as an empty composer.
- If UI Automation `ValuePattern` reports success but the composer stays empty, the app uses a guarded clipboard-paste fallback after revalidating the target.
- If an exact authorized resume draft already exists from a prior unconfirmed attempt, the app can submit it instead of blocking forever.

## Live Validation

Validated on 2026-09-13 against a selected active ChatGPT / Codex Work:

```yaml
Quota available: PASS
Correct active Work selected: PASS
Trusted interruption surface: PASS
Two confirmations: PASS
Pre-input claim: PASS
Input readback: PASS
Enter sent: PASS
Generation restarted: PASS
Duplicate journal Sent=true: PASS
```

## Safety Notes

v0.1 still only supports the currently open Active Work. It does not patrol or auto-switch between sidebar Works, because ChatGPT Desktop does not expose a stable public Work identity or deep link suitable for safe non-active Work automation.

Public builds still default to Dry Run. Real submission should only be enabled by users who understand the alpha status and have verified behavior in their local setup.
