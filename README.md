# mc-ai

Minecraft AI 一体化仓库：把 mindcraft 机器人、1.20.1 服务端配置和控制面板放在同一个仓库里。

## 目录

| 目录 | 内容 |
|---|---|
| `mindcraft/` | AI 机器人（mindcraft 源码与配置模板）。运行数据、日志、缓存、`keys.json` 都不入库 |
| `server/` | Minecraft **1.20.1** 服务端目录：只提交配置模板，`server.jar`、`libraries/`、`world/`、日志都在 .gitignore 里 |
| `pcl-dl/` | 下载、驱动 PCL2 启动器的辅助脚本（含截图与带界面测试） |
| `panel/` | 控制面板：像素风侧边栏，管理服务端与 AI 机器人（WinForms 壳 + Node 后端 + 前端三件套）。构建看 `panel/build.ps1`，使用说明看 `panel/README.txt` |

## 不入库的东西

`node_modules/`、`server/server.jar`、`server/libraries/`、`server/world/`、`PCL2/`、`mindcraft/keys.json`、`**/.env`、各处的 `dist/`。

也就是说：clone 下来能拿到**代码与配置模板**，但拿不到世界存档、服务端 jar、机器人密钥和构建产物——这些都要本地重新生成或在别处备份。

## 与旧仓库的关系

`panel/` 原本是独立仓库 [dkvkvk/mc-ai-control](https://github.com/dkvkvk/mc-ai-control)。2026-10-30 用 `git subtree` 以保留历史的方式并入本仓库：它原来的 8 条提交作为合并提交的第二父存在（`git log --graph` 可查，`git merge-base --is-ancestor fd5716e HEAD` 为真），旧仓库已归档为只读。
