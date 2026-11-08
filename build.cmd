@echo off
setlocal
set "GV_ROOT=%~dp0"
set "GV_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%GV_CSC%" exit /b 1
if not exist "%GV_ROOT%dist" mkdir "%GV_ROOT%dist"
"%GV_CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /debug- /langversion:5 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/win32manifest:%GV_ROOT%src\native\app.manifest" "/resource:%GV_ROOT%patches\GunValkyrie-Kr-v0.9.gvpatch,GV.patch" "/out:%GV_ROOT%dist\XBOX-GunValkyrie-Kr-v0.9-Patcher.exe" "%GV_ROOT%src\native\PatchEngine.cs" "%GV_ROOT%src\native\Program.cs"
if errorlevel 1 exit /b 1
"%GV_ROOT%dist\XBOX-GunValkyrie-Kr-v0.9-Patcher.exe" --self-test
exit /b %ERRORLEVEL%
