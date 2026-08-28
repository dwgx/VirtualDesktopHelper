# VirtualDesktopHelper (VDH)

Windows tool for official Virtual Desktop **Streamer** settings.  
It does **not** ship Quest APKs, keystores, or patches.

- Language: C# / .NET Framework 4 WinForms (`VDH.cs`)
- Author: [dwgx](https://github.com/dwgx)
- Feedback: csgowiki@qq.com
- Related APK notes: https://dwgx.github.io/VirtualDesktop/about.html

## Updates (OTA)

VDH checks only:

`https://api.github.com/repos/dwgx/VirtualDesktopHelper/releases/latest`

It downloads `VDH.exe` and `SHA256SUMS.txt` from that release, refuses any other host, and aborts unless the SHA-256 matches. There is no URL box (no SSRF / no random MITM download).

## Build

```
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Needs Windows `csc.exe` (.NET Framework 4.x).
