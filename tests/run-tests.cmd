@echo off
rem MIXLINE Scene Manager - offline unit tests
setlocal

if not defined WINDIR set WINDIR=C:\Windows
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set CSC=%FW%\csc.exe
set OUT=%~dp0..\bin\MsmTests.exe
set RSP=%TEMP%\msm-tests.rsp

if not exist "%CSC%" (
  echo [ERROR] csc.exe not found: %CSC%
  exit /b 1
)

if exist "%RSP%" del "%RSP%"
for /r "%~dp0..\src" %%f in (*.cs) do (
  echo "%%f" | findstr /i /c:"\Program.cs" >nul || echo "%%f">>"%RSP%"
)
for %%f in ("%~dp0*.cs") do echo "%%f">>"%RSP%"

"%CSC%" /nologo /codepage:65001 /target:exe /platform:x64 /out:"%OUT%" ^
  /r:"%FW%\System.dll" /r:"%FW%\System.Core.dll" /r:"%FW%\System.Drawing.dll" ^
  /r:"%FW%\System.Windows.Forms.dll" /r:"%FW%\System.Web.Extensions.dll" ^
  @"%RSP%"
if errorlevel 1 (
  echo [FAILED] test build failed
  exit /b 1
)

echo.
"%OUT%"
exit /b %errorlevel%