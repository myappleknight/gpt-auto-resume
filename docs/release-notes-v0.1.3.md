# GPT Auto Resume v0.1.3-alpha

This alpha changes the new-install default to match what most users expect from an auto-resume tool.

## What Changed

- New installs now enable real Active Work auto resume by default.
- Default settings are now:

```yaml
DryRun: false
SendEnter: true
AllowRealSubmit: true
ResumePolicy: AutomaticForegroundResume
```

- Existing users keep their current local settings when upgrading.
- The README now explains the app in simpler language and clearly says it is free.

## What This Means

For a new user, GPT Auto Resume can type the resume message and press Enter after quota recovers, as long as the currently open Work is allowed and all safety checks pass.

The app still does not patrol multiple sidebar Works in v0.1. It only operates on the currently open Active Work.

## Validation

```yaml
New-install real submit defaults: PASS
Existing safety gates retained: PASS
Runtime config smoke: DryRun=false, SendEnter=true, AllowRealSubmit=true
Tests: 221 passed / 0 failed
```
