# Zindle Dev Unity SDK

Zindle Dev Unity SDK (formerly Bugyard) lets playtesters file a rich bug report
from inside a Unity build. Press the configured hotkey, fill in the overlay, and
the SDK uploads a screenshot, recent logs, scene/player metadata, build/runtime
details, and any optional diagnostic context to your Zindle Dev backend.

> **Status: alpha (0.1.x).** The SDK is suitable for internal playtests and QA
> builds. Public APIs and UI may change between minor versions, and the built-in
> overlay is intentionally minimal. For live production builds, pin the package,
> review the payload, and gate initialization to the builds where you want report
> capture enabled.

## What you get

- Built-in report overlay opened by **F8** or `ZindleDev.Open()`.
- Headless reporting through `ZindleDev.Capture(...)` for custom UI, debug menus,
  and automated gameplay reports.
- Screenshot capture after the overlay hides itself, so the image shows the game.
- Recent Unity console logs with a bounded ring buffer.
- Automatic metadata: scene name, player position, build version, Unity version,
  SDK version, device specs, locale, timezone, and estimated FPS.
- Optional reporter identity, free-form `context`, `events.json`, `save_state`,
  and `memory_dump.gz` attachments for deeper diagnostics.
- Client-side payload limits, retry handling, idempotent `clientReportId`s, and an
  offline queue for transient failures.
- Editor tools for creating config assets, validating common mistakes, syncing
  package version, and sending a real connectivity test report.

## Requirements

- Unity **2021.3+**.
- Any **Active Input Handling** setting: legacy Input Manager, new Input System,
  or **Both**. If you do not use a Unity input backend, open the overlay yourself
  with `ZindleDev.Open()`.
- A Zindle Dev backend endpoint and project API key. The default endpoint is
  `https://api.zindle.dev`; set `endpoint` to your own backend base URL
  when using a different deployment.

## Install

In Unity, open **Window -> Package Manager -> + -> Add package from git URL...**
and paste:

```text
https://github.com/zindle-dev/unity-sdk.git
```

Or add the package to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.zindledev.sdk": "https://github.com/zindle-dev/unity-sdk.git"
  }
}
```

That short URL tracks the default branch and is useful while evaluating the SDK.
For repeatable CI or release builds, pin a commit hash or release tag instead:

```json
{
  "dependencies": {
    "com.zindledev.sdk": "https://github.com/zindle-dev/unity-sdk.git#<commit-or-tag>"
  }
}
```

After Unity resolves the package, you should see **Tools -> Zindle Dev** in the
Unity menu and the **Basic Usage** sample in Package Manager.

### Upgrading from Bugyard

The rebrand changes the package ID and C# names. Update existing integrations:

| Before | Now |
|--------|-----|
| `com.bugyard.sdk` | `com.zindledev.sdk` |
| `using BugyardSDK;` | `using ZindleDevSDK;` |
| `Bugyard.Init(...)`, `Bugyard.Open()`, `Bugyard.Capture(...)` | `ZindleDev.Init(...)`, `ZindleDev.Open()`, `ZindleDev.Capture(...)` |
| `BugyardConfig` | `ZindleDevConfig` |
| `Bugyard.Runtime` / `Bugyard.Editor` assembly references | `ZindleDev.Runtime` / `ZindleDev.Editor` |
| **Tools -> Bugyard** | **Tools -> Zindle Dev** |

Replace the old dependency in `Packages/manifest.json` with the new one; do not
install both packages together. Rename other `Bugyard*` type references to
`ZindleDev*`, including `BugyardClient`, `BugyardVersion`, and an imported
`BugyardBootstrap` sample. Reimport the updated **Basic Usage** sample if needed.
Use a commit or tag containing the rebrand; earlier commits use the old names.
Source code calls need updating; old C# names are not aliases.

Renamed scripts and assembly definitions retain their Unity `.meta` GUIDs, and
the config/runtime components include serialization migration attributes. Keep
existing config assets when updating. The default backend URL is now
`https://api.zindle.dev`. Existing assets keep their serialized `endpoint`, so
update that field to the new URL when using the hosted backend. Keep your own
endpoint for self-hosted deployments.

The `by_pk_*` API keys, report format, and
`{persistentDataPath}/Bugyard/queue` storage path are unchanged. Queued reports
remain tied to their original endpoint and API key; changing the endpoint does
not move those reports to the new destination.

## Quick start

1. Create a config asset with **Tools -> Zindle Dev -> Create Config Asset**.
2. Select the asset and set:
   - `apiKey`: your project key, for example `by_pk_test_xxx`.
   - `endpoint`: your backend base URL, for example `https://api.zindle.dev`.
     Do not include `/v1`; the SDK appends it.
3. Initialize once from a GameObject in your first scene:

```csharp
using UnityEngine;
using ZindleDevSDK;

public class Bootstrap : MonoBehaviour
{
    [SerializeField] private ZindleDevConfig config;

    void Awake() => ZindleDev.Init(config);
}
```

4. Assign the config asset to the `config` field in the Inspector.
5. Verify the end-to-end connection with **Tools -> Zindle Dev -> Send Test Report**.
   Success returns a dashboard link; failure reports the concrete auth, endpoint,
   validation, or network reason.
6. Enter play mode or run a build, press **F8**, fill in the overlay, and click
   **Send**.

### Without a config asset

For a quick spike, initialize with values directly:

```csharp
ZindleDev.Init("by_pk_test_xxx", "https://api.zindle.dev");
```

A config asset is recommended for real projects because it exposes capture
toggles, payload limits, hotkey behavior, and offline queue settings.

### Programmatic triggers

Open the built-in overlay from your own UI:

```csharp
myReportButton.onClick.AddListener(ZindleDev.Open);
```

Send a report without showing the overlay:

```csharp
ZindleDev.Capture(new ReportInput
{
    title = "I got stuck behind the bridge",
    description = "Could not move after jumping near the bridge.",
    severity = Severity.High,
    category = "bug",
    reporter = new ReporterInfo { name = "QA Bot" },
});
```

Pass a callback to handle success or failure:

```csharp
var report = new ReportInput
{
    title = "I got stuck behind the bridge",
    severity = Severity.High,
};

ZindleDev.Capture(report, result =>
{
    if (result.success)
        Debug.Log($"Filed {result.reportId}: {result.dashboardUrl}");
    else
        Debug.LogWarning($"Report failed: {result.message}");
});
```

Use `ZindleDev.IsInitialized`, `ZindleDev.IsOverlayOpen`, and
`ZindleDev.IsInputBlocked` when integrating with your own UI and input handling.

## Production readiness checklist

- Pin the package to a commit or release tag before using it in a reproducible
  build pipeline.
- Commit only test keys (`by_pk_test_*`) or empty config assets. Inject live keys
  at runtime or keep live-key config assets out of source control.
- Confirm the endpoint is the backend base URL with no trailing `/v1`.
- Run **Tools -> Zindle Dev -> Send Test Report** for each environment before
  giving a build to testers.
- Decide which builds call `ZindleDev.Init(...)`. Many teams enable it only for
  development, staging, closed beta, or QA builds.
- Review [what gets sent](docs/what-gets-sent.md), disable screenshots/logs if
  needed, and tune payload caps for your privacy and disk-budget requirements.
- If your game polls raw `Input.GetKey(...)` or the new Input System directly,
  gate that code on `!ZindleDev.IsInputBlocked` while the overlay is open.
- Keep `pauseWhileOpen` off unless pausing with `Time.timeScale = 0` is compatible
  with your game.

## API reference

Everything lives in the `ZindleDevSDK` namespace. The public surface is the static
`ZindleDev` class plus plain data types.

### `ZindleDev`

| Member | Signature | Description |
|--------|-----------|-------------|
| `Init` | `void Init(ZindleDevConfig config)` | Initialize once at startup with a config asset. Logs an error on a null config and warns on duplicate init. |
| `Init` | `void Init(string apiKey, string endpoint = null)` | Convenience initializer for prototypes. `endpoint` defaults to `https://api.zindle.dev`. |
| `Open` | `void Open()` | Open the built-in report overlay. No-op if already open. Logs an error if not initialized. |
| `Capture` | `void Capture(ReportInput report, Action<SendResult> onResult = null)` | Capture and send a report headless, bypassing the overlay. |
| `Shutdown` | `void Shutdown()` | Tear down the SDK, unhook the log handler, and destroy the runtime. Mainly useful for tests and reinitialization. |
| `IsInitialized` | `bool` (get) | True after `Init` and before `Shutdown`. |
| `IsOverlayOpen` | `bool` (get) | True while the report overlay is open. |
| `IsInputBlocked` | `bool` (get) | True while the overlay is swallowing gameplay input according to `blockGameplayInput`. |

### `ReportInput`

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `title` | `string` | `(no title)` | Short report summary. |
| `description` | `string` | `null` | Longer details or repro steps. |
| `expectedResult` | `string` | `null` | What the tester expected to happen. |
| `severity` | `Severity` | `Medium` | `Low`, `Medium`, `High`, or `Critical`. |
| `category` | `string` | `config.defaultCategory` | Report category, for example `bug`, `crash`, or `feedback`. |
| `reporter` | `ReporterInfo` | `null` | Optional tester identity. |
| `playerPosition` | `Vector3?` | main camera position | Override for the reported player position. |
| `context` | `Dictionary<string, object>` | `null` | Free-form app state serialized into `metadata.context`. |
| `events` | `byte[]` | `null` | Optional JSON attachment uploaded as `events.json`. |
| `saveState` | `byte[]` | `null` | Optional save/game-state attachment. |
| `saveStateIsJson` | `bool` | `false` | Upload `saveState` as JSON instead of raw bytes. |
| `memoryDump` | `byte[]` | `null` | Optional gzip attachment uploaded as `memory_dump.gz`. |

`ReporterInfo` carries optional `id`, `name`, and `email` strings.

### `SendResult`

| Field | Type | Description |
|-------|------|-------------|
| `success` | `bool` | Whether the report was accepted. |
| `httpStatus` | `long` | HTTP status of the final attempt, or `0` on a transport/network error. |
| `reportId` | `string` | Backend report id on success. |
| `status` | `string` | `created` or `already_exists` on success. |
| `dashboardUrl` | `string` | Link to the report in the dashboard on success. |
| `errorCode` | `string` | Backend error code on failure, for example `UNAUTHORIZED`. |
| `message` | `string` | Human-friendly message safe to show in UI. |
| `details` | `string` | Raw backend `details` field for logging/diagnostics. |
| `queuedForRetry` | `bool` | True when a transient failure was persisted to the offline queue. |

## Configuration reference

Fields on the `ZindleDevConfig` asset:

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `apiKey` | `string` | `""` | Project API key. Do not commit live keys. |
| `endpoint` | `string` | `https://api.zindle.dev` | Backend base URL, no trailing `/v1`. |
| `environment` | `string` | `development` | Environment label sent with every report. |
| `hotkey` | `KeyCode` | `F8` | Key that opens the overlay. |
| `captureScreenshot` | `bool` | `true` | Capture a screenshot when a report is created. |
| `captureLogs` | `bool` | `true` | Attach recent Unity console logs. |
| `maxLogLines` | `int` | `500` | Recent log lines kept in memory. |
| `pauseWhileOpen` | `bool` | `false` | Set `Time.timeScale = 0` while the overlay is open. |
| `blockGameplayInput` | `bool` | `true` | Block gameplay input while the overlay is open and expose `ZindleDev.IsInputBlocked`. |
| `defaultCategory` | `string` | `bug` | Category for reports that do not specify one. |
| `maxScreenshotBytes` | `int` | `5 MB` | Downscale or drop screenshots above this size. |
| `maxLogBytes` | `int` | `2 MB` | Trim older logs above this size. |
| `maxMetadataBytes` | `int` | `256 KB` | Cap on serialized metadata. |
| `maxContextBytes` | `int` | `16 KB` | Drop oversized `context` before upload. |
| `maxEventsBytes` | `int` | `512 KB` | Drop oversized `events` attachments before upload. |
| `maxSaveStateBytes` | `int` | `10 MB` | Drop oversized `saveState` attachments before upload. |
| `maxMemoryDumpBytes` | `int` | `100 MB` | Drop oversized `memoryDump` attachments before upload. |
| `enableOfflineQueue` | `bool` | `true` | Persist transient failures to disk and retry later. |
| `maxQueuedReports` | `int` | `50` | Max failed reports kept on disk per endpoint and API key. |
| `maxQueuedBytes` | `int` | `32 MiB` | Max total size in bytes of queued report files across all destinations. |

## What gets sent

A multipart `POST {endpoint}/v1/reports` with:

- `metadata` JSON: `clientReportId`, environment, build/engine/SDK version,
  scene name, player position, report body, optional reporter, optional context,
  device specs, and runtime info.
- `screenshot` PNG, unless screenshot capture is disabled or the image is dropped
  by size limits.
- `logs` text, unless log capture is disabled.
- Optional `events`, `save_state`, and `memory_dump` attachments when supplied
  through `ReportInput`.

Auth is `Authorization: Bearer <apiKey>`. `clientReportId` is stable across
retries, so the backend can deduplicate repeated uploads.

### Offline queue

When a report cannot be uploaded because the player is offline or the server
returns a 5xx, the SDK retries in-process and then saves the report under the
player persistent data path. Queued reports are retried on the next launch and
after the next successful send with the original endpoint and API key. Reports
from the legacy shared queue remain on disk subject to the disk budget.

The queue stores metadata, screenshot, and logs. Large diagnostic blobs
(`events`, `save_state`, `memory_dump`) are not persisted to disk for replay.
If replay still receives HTTP 429 after retrying, the SDK stops that pass and
keeps the report queued for a later attempt.

The SDK evicts the oldest queued reports to stay within the count and disk
limits. The disk limit includes JSON and Base64 overhead. A report that exceeds
the disk limit by itself is skipped. Replay loads one report at a time.

## Documentation

- [Quick start](docs/quick-start.md)
- [API reference](docs/api-reference.md)
- [Configuration](docs/configuration.md)
- [What gets sent](docs/what-gets-sent.md)
- [Changelog](CHANGELOG.md)

## Roadmap

- [ ] Themed UI overlay.
- [ ] More built-in adapters for player identity and game-state collection.
- [ ] Expanded examples for CI/release gating and self-hosted environments.

## Releasing

The SDK version lives in `package.json#version` and `ZindleDevVersion.Value`.
To release, bump `package.json`, then run **Tools -> Zindle Dev -> Sync Version from
package.json** to update `ZindleDevVersion.cs`. The editor logs an error on load if
the two ever drift.

## License

MIT - see [`LICENSE`](LICENSE).
