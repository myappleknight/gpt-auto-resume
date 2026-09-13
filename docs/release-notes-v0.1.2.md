# GPT Auto Resume v0.1.2-alpha

This alpha updates the Active Work path after the first successful live input + Enter validation.

## What Changed

- Fixed a red `Needs attention` state that could appear because remembered non-active Works cannot be safely auto-switched in v0.1.
- Old failed/unconfirmed resume claims now expire after a short safety lease, so one failed verification no longer blocks a later valid retry forever.
- Successful `Sent=true` records still suppress duplicate sends for the same interruption event.

## Current Scope

GPT Auto Resume v0.1 works only with the currently open ChatGPT / Codex Work that the user has explicitly allowed in the app.

Multi-Work sidebar patrol is not enabled in v0.1 because ChatGPT Desktop does not currently expose a stable public Work id or deep link that can safely identify and reopen non-active Works. Sidebar titles are display labels only.

## Validation

```yaml
Tests: 220 passed / 0 failed
Release build: PASS
Active Work live input + Enter: PASS on 2026-09-13
Multi-Work navigation: NOT SUPPORTED IN v0.1
```

## Safety Defaults

Public builds still default to Dry Run safety mode. Real submission should only be enabled by testers who understand the current Active Work-only scope.
