# Tessalume Web 控制台（macOS）

把原本 Windows-only 的 WPF 桌面应用，用**纯 Node.js（零依赖）**重写成了跨平台 Web 控制台。
它复用仓库里原有的主题资源与兼容层（CSS / JS / 兼容 profile），通过本机 CDP（Chrome DevTools Protocol）
为 **ChatGPT 桌面端**注入主题，无需任何 Windows 专属 API、也无需 .NET。

> 状态：已在 macOS（Apple Silicon，ChatGPT 桌面端 + 调试端口 9222）上完整验证可用。
> 网页可完成探测、应用、移除、深 / 浅色切换与状态验收。

## 原理

- ChatGPT 桌面端基于 Electron。用 `--remote-debugging-port` 启动后，会开放一个本机 HTTP + WebSocket 调试端口（CDP）。
- Web 服务通过 `http://127.0.0.1:<port>/json/list` 找到 `app://` 页面（ChatGPT 桌面端的渲染进程），再用 `Runtime.evaluate` 注入主题脚本。
- 应用前会先 `Page.reload` 清空全局作用域里残留的变量声明，再分块把主题资源暂存到页面、最后执行注入；这样可以避免大体积主题一次性塞满单条 WebSocket 消息导致的截断，也能规避「`Identifier 'xxx' has already been declared`」这类历史失败注入留下的暂时性死区问题。
- 全部逻辑（CDP 发现、主题校验、注入脚本拼装、指纹计算）与原 C# `Tessalume.Core` 行为一致，只是用 Node 重写。
- 完全不依赖 .NET SDK / WPF / PowerShell / COM，因此在 macOS / Linux 上也能运行。

## 在 Mac 上使用

### 1. 准备（只需一次）

确保已安装 Node.js（macOS 自带 `node` 即可，需 >= 21；Apple Silicon 默认 `v24`）：

```bash
node -v
```

### 2. 启动控制台

```bash
cd web
node server.js
# 或
npm start
```

启动后终端会显示：

```
Tessalume Web 控制台已启动
  本地访问: http://127.0.0.1:5173
```

用浏览器打开 **http://127.0.0.1:5173**。

### 3. 让 ChatGPT 开放调试端口

在「终端」里先**完全退出 ChatGPT**，再用调试端口重启它：

```bash
# 完全退出（若已在运行）
osascript -e 'quit app "ChatGPT"'

# 以调试端口启动（默认路径，如不同请自行调整）
# 用 nohup + disown 让 ChatGPT 在终端关闭后仍保持运行，日志写入 /tmp/chatgpt-debug.log
nohup /Applications/ChatGPT.app/Contents/MacOS/ChatGPT --remote-debugging-port=9222 >/tmp/chatgpt-debug.log 2>&1 &
disown
```

> 端口可改，`9222` 为默认。改了就在网页里把端口号同步修改，或通过环境变量 `TESSALUME_CDP_PORT` 设置。

### 4. 在网页里操作

1. 点击「探测连接」→ 看到「已连接 :9222」即成功。
2. 从主题列表选一个主题（内置主题随仓库发布；自定义主题放在 `~/.tessalume/themes/`）。
3. 点「应用选中主题」→ 控制台会先用调试端口触发页面重载、暂存资源、再注入主题；稍候即可在 ChatGPT 桌面端看到效果。
4. 可随时「移除主题」「切换深 / 浅色」「读取页面状态」验收。验收接口会回传 `applied`、当前 `themeId`、深 / 浅色状态，便于确认注入是否真正生效。

## 自定义主题

把主题目录（含 `manifest.json` 与 `entryPoints.script` 等）放到：

```
~/.tessalume/themes/<你的主题名>/
```

刷新网页即可看到。主题校验规则与 Windows 版一致（见 `lib/themes.js`）。

## 环境变量（可选）

| 变量 | 说明 | 默认 |
| --- | --- | --- |
| `PORT` | 控制台监听端口 | `5173` |
| `TESSALUME_CDP_PORT` | ChatGPT 调试端口 | `9222` |
| `TESSALUME_COMPAT` | 兼容层资源目录 | `../src/Tessalume.App/Compatibility` |
| `TESSALUME_THEMES` | 内置主题目录 | `../themes` |

## 与原项目的对应

| 原 C# 组件 | Node 版位置 |
| --- | --- |
| `ThemeRuntime` / `CdpSession` | `lib/cdp.js` |
| `LoopbackCdpDiscovery` | `lib/cdp.js` 的 `discoverCodex` / `probe` |
| `ThemePackageLoader` / `ThemeFingerprintCalculator` | `lib/themes.js` |
| `ThemePayloadBuilder` | `lib/themes.js` 的 `buildPayload` |
| `CompatibilityRuntimeComposer` | `server.js` 的 `composeRuntime` |
| WPF `MainWindow` + `CodexPackageLauncher` | `public/index.html` + 终端启动命令 |

> 说明：`src/Tessalume.Web`（早期用 .NET 做的 Web 版本）已废弃；本目录是最终的纯 Node 实现。
