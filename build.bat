@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo ==========================================
echo   DeskPet build  (x86 / Win7 compatible)
echo ==========================================
echo.

set CSC=
if exist "%WINDIR%\Microsoft.NET\Framework\v3.5\csc.exe" set CSC=%WINDIR%\Microsoft.NET\Framework\v3.5\csc.exe
if not defined CSC if exist "%WINDIR%\Microsoft.NET\Framework\v2.0.50727\csc.exe" set CSC=%WINDIR%\Microsoft.NET\Framework\v2.0.50727\csc.exe
if not defined CSC if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not defined CSC (
  echo [ERROR] csc.exe not found. Enable ".NET Framework 3.5" in Windows features.
  pause
  exit /b 1
)
echo [csc] %CSC%

if not exist "build" mkdir "build"

"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ /warn:3 ^
  /out:"build\DeskPet.exe" ^
  /win32icon:"assets\sprites\icon.ico" ^
  /resource:"assets\sprites\icon.ico",DeskPet.icon.ico ^
  /resource:"assets\sprites\crawl_0.png",DeskPet.crawl_0.png ^
  /resource:"assets\sprites\crawl_1.png",DeskPet.crawl_1.png ^
  /resource:"assets\sprites\crawl_2.png",DeskPet.crawl_2.png ^
  /resource:"assets\sprites\crawl_3.png",DeskPet.crawl_3.png ^
  /resource:"assets\sprites\kowtow_0.png",DeskPet.kowtow_0.png ^
  /resource:"assets\sprites\kowtow_1.png",DeskPet.kowtow_1.png ^
  /resource:"assets\sprites\kowtow_2.png",DeskPet.kowtow_2.png ^
  /resource:"assets\petname.txt",DeskPet.petname.txt ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  "src\Program.cs" "src\Native.cs" "src\Sprites.cs" "src\PetForm.cs"

if errorlevel 1 (
  echo.
  echo [FAIL] build error.
  pause
  exit /b 1
)

echo.
echo [OK] build\DeskPet.exe
endlocal
