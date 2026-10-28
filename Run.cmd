@echo off
setlocal
pushd "%~dp0"
if errorlevel 1 exit /b 1
echo Building and running the development version...
dotnet run --project "%~dp0src\Radio.Windows\Radio.Windows.csproj" -c Debug
if errorlevel 1 goto :failed
popd
exit /b 0

:failed
echo.
echo Build or launch failed. Close the previous development window before retrying.
pause
popd
exit /b 1
