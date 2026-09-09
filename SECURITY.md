# Security Policy

GPT Auto Resume is a local desktop helper. It should never collect account credentials, cookies, API keys, payment data, or full conversation logs.

## Reporting

Please open a private security advisory on GitHub if available, or contact the maintainer through the repository profile.

## Security expectations

- No telemetry by default
- No cloud sync
- No credential storage
- No fixed screen-coordinate typing as the primary path
- No resume action without target-window verification
- No repeated resume for the same usage-limit event
- No cookie or bearer-token scraping
- No sidebar title used as production conversation identity
- Fail closed when the active Work cannot be safely verified

## Alpha defaults

`v0.1.0-alpha` defaults to Dry Run safety mode. Real-world Active Work automatic resume E2E validation is still pending.
