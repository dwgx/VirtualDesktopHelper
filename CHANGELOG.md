# VirtualDesktopHelper changelog

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
