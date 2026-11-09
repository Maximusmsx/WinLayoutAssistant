@echo off
REM ============================================================
REM  WinLayoutAssistant - 一键构建 (在 Win10 上双击运行)
REM  1. 如未装 .NET 8 SDK, 先用 winget 装 (约 200MB, 一次即可)
REM  2. 发布为单个 exe: publish\WinLayoutAssistant.exe
REM ============================================================
where dotnet >nul 2>nul
if %errorlevel% neq 0 (
  echo [1/2] 未检测到 dotnet, 正在安装 .NET 8 SDK ...
  winget install Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements
  if %errorlevel% neq 0 (
    echo 安装失败, 请手动安装: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
  )
  echo 安装完成, 请关闭本窗口后重新双击 build.bat 继续构建。
  pause
  exit /b 0
)

echo [2/2] 正在构建 ...
cd /d %~dp0src
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "%~dp0publish"
if %errorlevel% neq 0 (
  echo 构建失败, 请把上面的错误发给 Muse。
  pause
  exit /b 1
)
echo.
echo 构建成功: %~dp0publish\WinLayoutAssistant.exe
echo 双击它即可运行, 托盘图标右键有一键平铺。
pause
