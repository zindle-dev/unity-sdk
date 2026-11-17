# Basic Usage sample

This sample shows the minimum runtime integration:

- initialize Zindle Dev from a `ZindleDevConfig` asset,
- open the built-in overlay from code, and
- file a headless report with optional context.

## Setup

1. Create a config asset with **Tools -> Zindle Dev -> Create Config Asset**.
2. Select it and set:
   - `apiKey`: your project key, for example `by_pk_test_xxx`.
   - `endpoint`: your backend base URL, for example `https://api.zindle.dev`.
     Do not include `/v1`.
3. Add an empty GameObject to your first scene.
4. Attach `ZindleDevBootstrap`.
5. Assign the config asset to the `config` field.
6. Run **Tools -> Zindle Dev -> Send Test Report** to verify the backend connection.
7. Enter play mode and press **F8** to open the report overlay.

## Script examples

`ZindleDevBootstrap.cs` includes:

- `Awake()` -> `ZindleDev.Init(config)`.
- `Update()` -> example `ZindleDev.IsInputBlocked` guard for your own input code.
- `OpenReportOverlay()` -> open the built-in overlay from a custom button.
- `ReportStuckPlayer()` -> file a report headless with `ZindleDev.Capture(...)`,
  including a small free-form `context` snapshot.
