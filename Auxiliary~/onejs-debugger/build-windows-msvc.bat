@echo off
setlocal enabledelayedexpansion

REM Build OnejsDebugger native plugin for Windows x64 (native MSVC).
REM Output: Plugins~\Windows\x86_64\quickjs_unity.dll
REM Run from a Visual Studio "x64 Native Tools Command Prompt".

cd /d "%~dp0"

set BUILD_DIR=build-windows-msvc
set OUT_DIR=Plugins~\Windows\x86_64

if exist "%BUILD_DIR%" rmdir /s /q "%BUILD_DIR%"
if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

cmake -B "%BUILD_DIR%" -A x64 -DCMAKE_BUILD_TYPE=Release || exit /b 1
cmake --build "%BUILD_DIR%" --config Release --target quickjs_unity || exit /b 1

copy /Y "%BUILD_DIR%\Release\quickjs_unity.dll" "%OUT_DIR%\quickjs_unity.dll" || exit /b 1

echo.
echo DONE. %OUT_DIR%\quickjs_unity.dll
endlocal
