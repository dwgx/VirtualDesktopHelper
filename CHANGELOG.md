# VirtualDesktopHelper changelog

## 0.4.6 — 2026-08-28

- Shipped EN/ZH APKs use stock IL cap **500**.
- Home → Write IL picks a Quest APK, applies obfuscated Mobile.dll immediates, zipalign+sign → `*_capN.apk`.
- Wiki + `AGENT.md` for later agents. Feedback dump is short.
- Check for updates: real `v0.4.6` release; shows already-latest when current.

## 0.4.5 — 2026-08-28

- OTA: strip junk from VERSION.txt; publish a real `v0.4.5` release so 0.4.3 can update.
- Device name is read-only.
- Bitrate row shows IL status (repo DLL vs known APK SHA). No cryptic Mobile.dll popup.
- Headset grant only runtime perms; logcat no longer dumps kernel `audit: rate limit`.

## 0.4.4 — 2026-08-28

- adb: search SDK / PATH / VIVE / repo `quest_adb_tools`, never fall back to a bare `adb` on PATH.
- If missing: browse, or download Google `platform-tools` from `dl.google.com` only.
- Remember the chosen path in `%AppData%\VirtualDesktopHelper\config.json`.

## 0.4.1 — 2026-08-28

- OTA no longer calls `api.github.com` (that 403s when the anonymous rate limit is hit).
- Uses `github.com/.../releases/latest/download/{VERSION.txt,SHA256SUMS.txt,VDH.exe}` plus SHA-256.
- VDH tab layout: stacked labels and auto-sized buttons (no stretched empty bar).

## 0.4.0 — 2026-08-28

- Home tab is Streamer settings (right-hand codec essay moved to Wiki).
- Wiki page (HTML) for pairing, codecs, every setting.
- VDH tab: open `%AppData%\VirtualDesktopHelper`, optional GitHub OTA.
- OTA: `https://api.github.com/repos/dwgx/VirtualDesktopHelper/releases/latest` only. HTTPS. Redirect host allow-list. SHA-256 of `VDH.exe` must appear in `SHA256SUMS.txt`. No user-supplied URLs.
- Install from folder: SHA must be in our list (`8DEEF4FF…` current). Unknown APK refused.
- Bitrate row shows the Quest IL cap (this freeze 960). Streamer JSON has no MaxBitrate.
- Icon: monitor PNG + `helper` badge.
- Headset APK zips no longer include VDH.

## 0.3 — 2026-08-28

- Clear history, Guide/Headset tabs, mail feedback.

## 0.2 — 2026-08-28

- Human account list. Unified codec dropdown.
