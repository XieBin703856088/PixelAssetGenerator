@echo off
setlocal enabledelayedexpansion

REM 设置项目路径（当前目录）
set PROJECT_PATH=.
set OUTPUT_PATH=.\bin\Release

REM 获取当前版本号（从.csproj文件或创建新的）
if not exist version.txt (
    echo 0.5.1.0 > version.txt
)

for /f "delims=" %%i in (version.txt) do set VERSION=%%i

REM 分割版本号并递增
for /f "tokens=1-4 delims=." %%a in ("%VERSION%") do (
    set MAJOR=%%a
    set MINOR=%%b
    set BUILD=%%c
    set /a REVISION=%%d+1
)

set NEW_VERSION=%MAJOR%.%MINOR%.%BUILD%.%REVISION%

REM 保存新版本号
echo %NEW_VERSION% > version.txt

echo 正在编译版本: %NEW_VERSION%

REM 编译32位版本
echo 编译32位版本...
dotnet publish "%PROJECT_PATH%" ^
    -c Release ^
    -r win-x86 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=None ^
    -p:CopyOutputSymbolsToPublishDirectory=false ^
    -p:AssemblyVersion=%NEW_VERSION% ^
    -p:FileVersion=%NEW_VERSION% ^
    -p:Version=%NEW_VERSION% ^
    --output "%OUTPUT_PATH%\win-x86"

REM 编译64位版本
echo 编译64位版本...
dotnet publish "%PROJECT_PATH%" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=None ^
    -p:CopyOutputSymbolsToPublishDirectory=false ^
    -p:AssemblyVersion=%NEW_VERSION% ^
    -p:FileVersion=%NEW_VERSION% ^
    -p:Version=%NEW_VERSION% ^
    --output "%OUTPUT_PATH%\win-x64"

REM 重命名文件 - 使用简单的方法
if exist "%OUTPUT_PATH%\win-x86\*.exe" (
    for /f "delims=" %%i in ('dir /b "%OUTPUT_PATH%\win-x86\*.exe"') do (
        set "filename=%%~ni"
        rename "%OUTPUT_PATH%\win-x86\%%i" "!filename!_x86_v%NEW_VERSION%%%~xi"
    )
)

if exist "%OUTPUT_PATH%\win-x64\*.exe" (
    for /f "delims=" %%i in ('dir /b "%OUTPUT_PATH%\win-x64\*.exe"') do (
        set "filename=%%~ni"
        rename "%OUTPUT_PATH%\win-x64\%%i" "!filename!_x64_v%NEW_VERSION%%%~xi"
    )
)

echo.
echo 编译完成!
echo 版本: %NEW_VERSION%
echo 32位文件: %OUTPUT_PATH%\win-x86\*_x86_v%NEW_VERSION%.exe
echo 64位文件: %OUTPUT_PATH%\win-x64\*_x64_v%NEW_VERSION%.exe

pause