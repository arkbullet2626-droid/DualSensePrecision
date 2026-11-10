@echo off
rem Double-click to build DualSense Precision and its Setup.exe.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1"
echo.
pause
