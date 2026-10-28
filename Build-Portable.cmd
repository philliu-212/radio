@echo off
setlocal
pushd "%~dp0"
if errorlevel 1 goto :failed

echo Building Lite package...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Publish-Portable.ps1"
if errorlevel 1 goto :failed

echo.
echo Building Full package...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Publish-Portable.ps1" -SelfContained
if errorlevel 1 goto :failed

echo.
echo Build complete. Both packages and ZIP files are in dist.
start "" "%~dp0dist"
popd
pause
exit /b 0

:failed
echo.
echo Build failed. Review the error above. .NET 8 SDK is required.
pause
exit /b 1
