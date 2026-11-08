@echo off
setlocal
set "GV_ROOT=%~dp0.."
set "GV_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%GV_ROOT%\build" mkdir "%GV_ROOT%\build"
"%GV_CSC%" /nologo /target:exe /main:NativeTests /langversion:5 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/out:%GV_ROOT%\build\NativeTests.exe" "%GV_ROOT%\src\native\PatchEngine.cs" "%GV_ROOT%\src\native\Program.cs" "%~dp0NativeTests.cs"
if errorlevel 1 exit /b 1
"%GV_ROOT%\build\NativeTests.exe"
exit /b %ERRORLEVEL%
