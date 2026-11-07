@echo off
rem MIXLINE Scene Manager - build script (single exe, no third-party deps)
setlocal

if not defined WINDIR set WINDIR=C:\Windows
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set CSC=%FW%\csc.exe
set OUT=%~dp0bin\MIXLINE Scene Manager.exe

if not exist "%CSC%" (
  echo [ERROR] csc.exe not found: %CSC%
  exit /b 1
)

if not exist "%~dp0bin" mkdir "%~dp0bin"

set SRC_LIST=%TEMP%\msm-sources.rsp
if exist "%SRC_LIST%" del "%SRC_LIST%"
for /r "%~dp0src" %%f in (*.cs) do echo "%%f">>"%SRC_LIST%"

"%CSC%" /nologo /codepage:65001 /target:winexe /platform:x64 ^
  /win32manifest:"%~dp0app.manifest" /out:"%OUT%" ^
  /r:"%FW%\System.dll" /r:"%FW%\System.Core.dll" /r:"%FW%\System.Drawing.dll" ^
  /r:"%FW%\System.Windows.Forms.dll" /r:"%FW%\System.Web.Extensions.dll" ^
  @"%SRC_LIST%"

if errorlevel 1 (
  echo.
  echo [FAILED] compile error
  exit /b 1
)

if exist "%~dp0app.config" copy /Y "%~dp0app.config" "%~dp0bin\MIXLINE Scene Manager.exe.config" >nul

echo.
echo [OK] %OUT%
exit /b 0