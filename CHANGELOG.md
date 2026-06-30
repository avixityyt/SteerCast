# Changelog

## 0.1.8 - 2026-07-05

- Added the new static PNG and animated GIF SteerCast logo assets.
- Regenerated the Windows app/tray/installer icon from the new logo.
- Switched setup branding to use the static logo and startup splash to use the animated logo.
- Removed old SVG/preview logo files and logo exploration outputs.
- Limited packaged branding files to only runtime assets.

## 0.1.7 - 2026-07-01

- Fixed setup preview stretching after OBS canvas size changes.
- Rescaled and clamped module positions when changing overlay width or height.
- Normalized loaded profiles in the setup editor so previously-bad saved positions recover cleanly.

## 0.1.6 - 2026-07-01

- Removed the shifter Gear label and aligned pedal pressure rails tightly beside each pedal.
- Made release packaging stop on failed native build, test, publish, or installer commands.

## 0.1.5 - 2026-07-01

- Moved pedal pressure indicators into vertical rails beside each pedal on the image-backed overlay.

## 0.1.4 - 2026-07-01

- Fixed WebView2 setup launch by running the desktop UI entrypoint on an explicit STA thread.

## 0.1.3 - 2026-07-01

- Open setup inside the SteerCast app with WebView2 instead of launching the external browser.
- Reworked overlay readouts: visual steering gauge, visible pedal pressure bars, and a cleaner transparent shifter presentation.
- Reorganized frontend entrypoints/styles and fixed corrupted button glyph encoding.
- Added README install/build/architecture/FAQ guidance and a pull request template.

## 0.1.2 - 2026-06-29

- Cache-busted setup and overlay assets so Chrome/OBS cannot keep rendering an older bundle after upgrade.
- Migrated legacy v1 profiles to the polished default overlay layout and saved the repaired profile back to disk.

## 0.1.1 - 2026-06-29

- Fixed false stale input warnings when wheel values are not changing.
- Normalized Windows Gaming Input device IDs for reliable saved wheel matching.
- Kept overlay assets readable during disconnected/stale states.
- Added layout recovery for invalid saved module positions.

## 0.1.0 - 2026-06-29

- Initial Windows companion app, OBS overlay, setup UI, installer, and portable package.
