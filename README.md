# VirtualDesktopHelper

C# WinForms app (`.NET Framework 4.8`) for [Virtual Desktop](https://www.vrdesktop.net) **Streamer** settings on Windows.

It does **not** ship Quest APKs, keystores, or IL patches.

- Author: [dwgx](https://github.com/dwgx)
- Feedback: csgowiki@qq.com
- APK notes: https://dwgx.github.io/VirtualDesktop/about.html

## Build

Visual Studio 2022 (workload: **.NET desktop development**) or:

```bat
dotnet build VirtualDesktopHelper.sln -c Release
```

Output: `VirtualDesktopHelper\bin\Release\VDH.exe`

Double-click `VDH.bat` to build if needed and run.

## Project

```
VirtualDesktopHelper.sln
VirtualDesktopHelper/
  VirtualDesktopHelper.csproj
  VDH.cs
  VDH.Extra.cs
  VDH.ico
  app.manifest
```

## Updates

Release assets are `VDH.exe` + `SHA256SUMS.txt` + `VERSION.txt`.
The app reads version from `raw.githubusercontent.com/dwgx/VirtualDesktopHelper/main/VERSION.txt`
and downloads only `https://github.com/dwgx/VirtualDesktopHelper/releases/download/vX.Y.Z/...`
(HTTPS, host allow-list, SHA-256). No URL box.
