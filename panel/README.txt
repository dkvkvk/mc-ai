Minecraft AI 控制面板
=====================

一个把「Minecraft 服务端 + AI 机器人（mindcraft）」管起来的中文侧边栏面板。
像素风界面，参考了 GitHub 上的 MC 风格前端（aubergine-ux/MC-Website-Template、
katorlys/OreUI）的做法：0 圆角 + 硬边框 + 内嵌高光做方块立体感。

这是一个真正的桌面程序：窗口由 MC-AI-Control.exe 自己创建（WinForms + WebView2，
Edge 内核嵌在程序里），有自己的图标、标题栏和任务栏项，没有地址栏、没有标签页。

用法
----
1. 双击 MC-AI-Control.exe
2. 独立窗口打开面板
3. 「概览」里点「启动服务器」→ 等 10~30 秒 → 再点「启动全部（机器人）」
4. 去 PCL2 连接 127.0.0.1:25565
5. 在「快速派活」或「AI 机器人」页直接打字下指令

多开机器人
----------
「AI 机器人」页可以同时挂多个机器人：

- 在「添加机器人」里填名字（3~16 位字母/数字/下划线）→ 点「添加并启动」
- 每个机器人是独立进程，共用一个服务端和一个 MindServer
- 每一行都能单独「启动 / 停止 / 删除」；也可以「启动全部 / 停止全部」
- 派活时用左边的下拉选「发给全部」或某个具体机器人
- 所有机器人都共享同一套模型配置，换模型就改那一行的「模型 / 接口地址」

原理：MindServer(8080) 支持 create-agent / start-agent / stop-agent / destroy-agent
事件，面板只跑一个 MindServer，机器人动态创建，所以多开不需要多开面板、也不会
互相顶号。

> 发言固定用 en 发出。填 zh 会让 mindcraft 调 google-translate-api-x 把每句话翻译
> 一遍，国内基本连不上，还会把报错刷进聊天。机器人本来就跟着你的提问语言回答。

运行前提
--------
- Windows 10/11（64 位）
- WebView2 运行时：Win11 自带；Win10 装了新版 Edge 也有。缺了会提示，
  并自动退回「用 Edge 应用窗口打开」，功能不受影响
- node.exe：优先用 exe 同目录自带的 node.exe（打包时已附带）；
  没带的话依次找 PATH、D:\nodejs、Program Files，都找不到就把路径写进
  <数据目录>\node.txt
- Java 17（跑 1.20.1 服务端）

文件都在哪
----------
数据目录按顺序挑第一个「能写的」：
  1. %LOCALAPPDATA%\MC-AI-Control
  2. exe 同目录\MC-AI-Control-data        ← 便携模式
  3. %TEMP%\MC-AI-Control

- 程序解压目录：<数据目录>\app
- 面板配置：    <数据目录>\app\data\config.json
- 机器人清单：  <数据目录>\app\data\bots.json
- 机器人日志：  <数据目录>\app\data\mindserver.log
- 窗口内容：    <数据目录>\webview
- 启动日志：    <数据目录>\launcher.log（写不进去会落到 exe 同目录或 %TEMP%）

打不开怎么办
------------
1. 双击后先等 5~15 秒（要拉起 node.js 后端），别急着重复点
2. 命令行运行 `MC-AI-Control.exe --doctor`，会生成 doctor.txt 并打开，
   里面写着 node / WebView2 / Edge / 端口 / 三处目录可写性 / 已装文件时间戳
3. 看 launcher.log 最后几行，出错原因都写在那
4. 还不行就双击同目录的 启动面板.bat，它绕过 exe 直接跑 node server.js

几个说明
--------
- 窗口关闭时，本程序启动的后端会一起退出，不留后台进程。
- 关面板**不会**杀掉服务器和机器人（方便你关面板继续挂机）：
  MindServer 的输出写到文件而不是管道，管道断开也不会把它带走。
  下次打开面板会显示为「外部启动」，此时面板不能远程关服务器——
  要关请在服务器控制台输入 stop；机器人可以点「关闭 MindServer」。
- 「停止」按钮只在「本面板启动的」服务器上可用，这是有意的安全设计。
- 端口默认：服务端 25565，MindServer 8080，面板后端 4599（被占用会自动往后找）。
