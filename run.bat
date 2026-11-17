@echo off
cd /d "%~dp0"

echo ========================================================
echo   Chat Helper Starting...
echo   - Default Hotkey: Alt + Q
echo   - Tray Icon: Check system tray (bottom-right)
echo ========================================================

if not exist ".venv\Scripts\python.exe" (
    echo [ERROR] .venv not found.
    pause
    exit /b
)

".venv\Scripts\python.exe" main.py
if errorlevel 1 pause
