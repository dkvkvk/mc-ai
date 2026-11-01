@echo off
rem ============================================================
rem  MC-AI 控制面板 —— 免 exe 的启动脚本
rem  什么时候用：双击 MC-AI-Control.exe 没反应，或被杀软拦住。
rem  它做的事和 exe 一样：起 node 后端 -> 等端口 -> 开 Edge 应用窗口。
rem ============================================================
setlocal
cd /d "%~dp0"

set "NODE="
where node >nul 2>nul && set "NODE=node"
if not defined NODE if exist "D:\nodejs\node.exe" set "NODE=D:\nodejs\node.exe"
if not defined NODE if exist "%ProgramFiles%\nodejs\node.exe" set "NODE=%ProgramFiles%\nodejs\node.exe"
if not defined NODE (
  echo [x] 没找到 node.exe，请先安装 Node.js: https://nodejs.org
  pause
  exit /b 1
)

echo [1/3] 启动后端：%NODE% server.js
start "MC-AI 后端" /min "%NODE%" server.js

echo [2/3] 等后端监听 4599 ...
for /l %%i in (1,1,30) do (
  powershell -NoProfile -Command "try{(New-Object Net.Sockets.TcpClient).Connect('127.0.0.1',4599);exit 0}catch{exit 1}" >nul 2>nul
  if not errorlevel 1 goto :ready
  timeout /t 1 >nul
)
echo [x] 后端没起来。请看那个最小化的「MC-AI 后端」窗口里的报错。
pause
exit /b 1

:ready
echo [3/3] 打开面板窗口
set "EDGE=%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"
if not exist "%EDGE%" set "EDGE=%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"
if exist "%EDGE%" (
  start "" "%EDGE%" --app=http://127.0.0.1:4599 --window-size=1200,860 --user-data-dir="%LOCALAPPDATA%\MC-AI-Control\browser" --no-first-run --no-default-browser-check
) else (
  start "" http://127.0.0.1:4599
)
echo 已启动。这个窗口可以直接关掉（后端在另一个最小化窗口里跑）。
timeout /t 3 >nul
exit /b 0