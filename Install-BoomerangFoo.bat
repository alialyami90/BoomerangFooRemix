@echo off
rem One-click install/update for Boomerang Foo: Chaos Mod (remix).
rem Keep this file anywhere (e.g. your Desktop) and double-click it.
rem It fetches the latest installer script and mod source from GitHub, builds the mod and installs it.
setlocal
set "REPO=alialyami90/BoomerangFooRemix"
set "BRANCH=claude/magical-carson-63p7ji"
set "SCRIPT=%TEMP%\install-boomerangfoo.ps1"

echo Downloading the latest installer...
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -UseBasicParsing -Uri 'https://raw.githubusercontent.com/%REPO%/%BRANCH%/tools/install-mod.ps1' -OutFile '%SCRIPT%'"
if errorlevel 1 (
    echo.
    echo Could not download the installer. Check your internet connection.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -Branch "%BRANCH%"
echo.
pause
