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

## For later agents

- EN/ZH Quest APKs ship with stock slider cap **500**. Do not bake 960.
- Bitrate lives in Quest `Mobile.dll` IL (`0x13B0C` `0x14E45` `0x2BFE3` on 22), not Streamer JSON.
- VDH **Write IL** patches a user-selected APK (offsets XOR-masked in `BitrateApk.cs`) then zipalign+apksigner.
- No Path C on 22. HorizonOS: Open app, never Restore. Pairing name `dwgx`.
- `VERSION.txt` must be only `x.y.z` plus newline. Release must include `VDH.exe` + `SHA256SUMS.txt`.

## Updates

Release assets are `VDH.exe` + `SHA256SUMS.txt` + `VERSION.txt`.
The app reads version from `raw.githubusercontent.com/dwgx/VirtualDesktopHelper/main/VERSION.txt`
and downloads only `https://github.com/dwgx/VirtualDesktopHelper/releases/download/vX.Y.Z/...`
(HTTPS, host allow-list, SHA-256). No URL box.
