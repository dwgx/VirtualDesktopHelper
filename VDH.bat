@echo off
cd /d "%~dp0"
if not exist VDH.exe (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
  if errorlevel 1 pause & exit /b 1
)
start "" "%~dp0VDH.exe"
