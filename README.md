# mc-ai

Minecraft AI 一体化仓库：把 mindcraft 机器人、1.20.1 服务端配置和控制面板放在同一个仓库里。

## 目录

| 目录 | 内容 |
|---|---|
| `mindcraft/` | AI 机器人（mindcraft 源码与配置模板）。运行数据、日志、缓存、`keys.json` 都不入库 |
| `server/` | Minecraft **1.20.1** 服务端目录：只提交配置模板，`server.jar`、`libraries/`、`world/`、日志都在 .gitignore 里 |
| `panel/` | 控制面板：像素风侧边栏，管理服务端与 AI 机器人（**WinForms + WebView2 桌面程序** + Node 后端 + 前端三件套）。构建看 `panel/build.ps1`，使用说明看 `panel/README.txt` |

## 从 clone 到能玩

1. **装依赖**（`node_modules/` 不入库，这步跳过机器人就起不来）：

   ```bash
   cd mindcraft
   npm install
   ```

   `postinstall` 会跑 `patch-package`，自动打上 `patches/` 里 6 个补丁（mineflayer、pathfinder、protodef…）。`canvas` 已从依赖里摘掉，不需要 MSVC。

2. **补密钥**：`mindcraft/keys.json` 不入库，按 `keys.example.json` 填自己的 key（当前用的是 `DEEPSEEK_API_KEY` 指向 Cline 网关）。
3. **补服务端 jar**：`server/server.jar`（1.20.1）也不入库。走 BMCLAPI 镜像下，`piston-data.mojang.com` 直连会超时。`server.properties` 里 `online-mode=false` + `enforce-secure-profile=false` 都必需。
4. **面板（安装包）**：**不本地编译**——改完代码推上来，[build-panel 工作流](.github/workflows/build-panel.yml) 会在 GitHub 的 Windows runner 上用 `csc` 编译，产物在 Actions 页面下载：

   - `MC-AI-Control-Setup.exe` —— 带安装向导（欢迎 → 许可协议 → 安装位置 → 安装 → 完成），装完有开始菜单快捷方式、桌面快捷方式，并在「设置 → 应用」里有一条可卸载项。装到当前用户目录，**不需要管理员权限**。
   - `MC-AI-Control-portable.zip` —— 解压即用，配置跟着目录走。

   两个产物都自带 `node.exe`，目标机不需要另装 Node。打 `v*` 标签会同时发一个 Release。
   非要本地编：`powershell -ExecutionPolicy Bypass -File panel/build.ps1 -BundleNode`（首次会从 nuget 拉 WebView2 SDK 到 `panel/.cache/`）。
5. **路径**：面板首次启动会从 exe 所在目录逐级向上找 `server/`（含 `server.jar`）和 `mindcraft/`（含 `main.js`）并写进 `data/config.json`；找不到就去「设置」页手填。

> 关面板不杀服务器和机器人（日志写文件、不走管道），下次打开会识别为「外部启动」。日志只跟新写入的行，不会回放上一局。

**面板打不开时**：

- 真相先看日志：`launcher.log` 在数据目录里，写不进去会依次落到 exe 同目录、`%TEMP%`。
- 数据目录就在**安装目录**里：`<exe 所在目录>\MC-AI-Control-data`（配置、日志、机器人清单、WebView2 缓存全在里面）。装到哪个盘就留在哪个盘，不写 C 盘的 AppData / 用户目录。只有在安装目录不可写时（例如装进 Program Files）才退到 `%LOCALAPPDATA%\MC-AI-Control`，再不行退 `%TEMP%`，落在哪会写进 `launcher.log`。
- `MC-AI-Control.exe --doctor` 生成 `doctor.txt`（node / WebView2 / Edge / 端口 / 目录可写性 / 已安装文件时间戳）并用记事本打开。

## 不入库的东西

`node_modules/`、`server/server.jar`、`server/libraries/`、`server/world/`、`PCL2/`、`mindcraft/keys.json`、`**/.env`、各处的 `dist/`。

也就是说：clone 下来能拿到**代码与配置模板**，但拿不到世界存档、服务端 jar、机器人密钥和构建产物——这些都要本地重新生成或在别处备份。

## 与旧仓库的关系

`panel/` 原本是独立仓库 [dkvkvk/mc-ai-control](https://github.com/dkvkvk/mc-ai-control)。2026-10-30 用 `git subtree` 以保留历史的方式并入本仓库：它原来的 8 条提交作为合并提交的第二父存在（`git log --graph` 可查，`git merge-base --is-ancestor fd5716e HEAD` 为真），旧仓库已归档为只读。
