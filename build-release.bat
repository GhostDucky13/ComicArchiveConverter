@echo off
rem ==========================================================================
rem  Double-click this file to build the Windows release of Comic Archive Converter.
rem
rem  It runs build-release.ps1, which:
rem    1. runs the automated tests
rem    2. publishes the .exe with every DLL it needs (no .NET install required)
rem    3. adds README.txt, LICENSE.txt and THIRD-PARTY-NOTICES.txt
rem    4. zips them as publish\ComicArchiveConverter-v<version>-windows.zip (every PC)
rem       and publish\ComicArchiveConverter-v<version>-win-x64.zip (64-bit PCs)
rem
rem  Needs the .NET 8 SDK, which Visual Studio 2022 installs for you.
rem ==========================================================================
setlocal
cd /d "%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-release.ps1" %*
if errorlevel 1 (
    echo.
    echo BUILD FAILED - read the red error messages above.
    pause
    exit /b 1
)

explorer "%~dp0publish"
pause
