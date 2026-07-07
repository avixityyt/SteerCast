# SteerCast

SteerCast is a lightweight Windows companion app that shows Logitech-style racing wheel inputs in OBS through a local Browser Source.

The app runs a small localhost server, opens setup inside the SteerCast desktop window, and gives OBS a stable overlay URL such as:

```text
http://127.0.0.1:38271/overlay/default
```

## Installation

1. Download and run the SteerCast installer.
2. Launch SteerCast.
3. Use the in-app setup window to select/calibrate your wheel and copy the OBS URL.
4. In OBS, add a Browser Source and paste the copied URL.

Recommended OBS source size:

```text
800 x 600 at 60 FPS
```

SteerCast uses Microsoft Edge WebView2 for the in-app setup window. Current Windows 10/11 installs usually already include it. If setup cannot open inside the app, repair/install the Microsoft Edge WebView2 Runtime and reopen setup from the tray icon.

## Building from source

Prerequisites:

- Windows 10/11 x64
- PowerShell
- .NET 10 SDK. This repo can use the local `.dotnet` SDK cache when present.
- Node/npm
- Inno Setup if you want the installer output

Build, test, publish, and package:

```powershell
.\scripts\package.ps1
```

Outputs are written to:

```text
artifacts/
```

Useful maintenance commands:

```powershell
.\scripts\clean.ps1
.\scripts\publish-release.ps1
```

## How it works

- `src/SteerCast.App` is the Windows tray app, local HTTP/WebSocket server, device capture host, and WebView2 setup shell.
- `src/SteerCast.Core` contains input normalization, calibration, profile models, Logitech presets, and changed-frame filtering.
- `web/src/setup` is the Preact setup/editor UI.
- `web/src/overlay` is the framework-free OBS overlay renderer.
- Profiles are persisted as JSON under `%LocalAppData%\SteerCast\profiles`.
- OBS connects to `127.0.0.1` only. No cloud service, database, OBS plugin, or Electron runtime is used.

Input flow:

```text
Wheel hardware -> Windows Gaming Input -> normalization/calibration -> WebSocket frames -> OBS overlay DOM transforms
```

The overlay uses bundled G920 imagery from:

```text
web/public/assets/g920
```

Device artwork, product likenesses, names, and marks are owned by their respective owners. SteerCast is not affiliated with or endorsed by Logitech.

## FAQ

### Does SteerCast require an OBS plugin?

No. Add one OBS Browser Source and paste the local overlay URL.

### Does it work with G29, G920, and G923?

The app includes built-in Logitech presets for G29/G920/G923-style mappings. Hardware verification should still be done before release on each wheel, pedals, and optional H-pattern shifter.

### Why is setup inside the app, but OBS still uses a URL?

Setup is shown in a native WebView2 window so users do not need to open a browser. OBS still needs a Browser Source URL because that is the cleanest plugin-free integration.

### Does it read force-feedback strength from games?

No. Windows Gaming Input does not expose the live force magnitude produced by another game process, so SteerCast does not show a misleading force-feedback meter.

### Where are settings stored?

Profiles are stored in:

```text
%LocalAppData%\SteerCast\profiles
```

### Why does an old layout still appear after installing?

Versioned profile migrations reset old layouts when the overlay schema changes. If you manually copied profiles between installs, delete or edit the matching JSON profile in `%LocalAppData%\SteerCast\profiles`.

## Contributing

Use the pull request template in `.github/PULL_REQUEST_TEMPLATE.md`.

For any visual or input change, include:

- what changed;
- what hardware/profile was tested;
- screenshots or short clips for UI changes;
- test commands run;
- known limitations or follow-up work.

Keep the overlay fast: no frontend polling, no framework rerender per input frame, no Electron, and no unnecessary layout animation.

## License

MIT for SteerCast source code. Bundled device artwork remains owned by its respective owners. See `LICENSE` and `THIRD_PARTY_NOTICES.md`.
