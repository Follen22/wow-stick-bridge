@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe not found: %CSC%
  pause
  exit /b 1
)
"%CSC%" /nologo /target:exe /platform:x64 /optimize+ /out:"%~dp0WoWStickBridge.exe" "%~dp0WoWStickBridge.cs"
if errorlevel 1 (
  echo BUILD FAILED
  pause
  exit /b 1
)
echo OK: %~dp0WoWStickBridge.exe
pause
