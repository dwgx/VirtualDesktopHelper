@echo off
cd /d "%~dp0"
set EXE=%~dp0VirtualDesktopHelper\bin\Release\VDH.exe
if not exist "%EXE%" (
  dotnet build "%~dp0VirtualDesktopHelper.sln" -c Release
  if errorlevel 1 (
    echo Install Visual Studio 2022 with .NET desktop development, or the .NET SDK.
    pause
    exit /b 1
  )
)
start "" "%EXE%"
