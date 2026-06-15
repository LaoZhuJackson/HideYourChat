# HideYourChat

一个 Windows 桌面悬浮助手，让你无需离开当前工作窗口即可查看和快捷回复聊天消息。

**两大核心场景**：全屏工作时不想切窗口看消息、上班摸鱼时保持隐蔽。

## 功能

- **悬浮消息窗** — 半透明置顶窗口，可折叠为小浮球，支持拖拽、缩放
- **消息实时轮询** — 可配置轮询间隔，新消息自动显示
- **快捷回复** — 悬浮窗内置输入框，Enter 发送，不切窗口
- **深色 / 浅色主题** — 跟随系统或手动切换
- **背景 / 文字透明度** — 独立滑块调节

## 支持的聊天应用

| 应用 | 技术方案 | 状态 |
|---|---|---|
| **QQ / TIM** | FlaUI UIA3 直读控件树 | 消息读写、窗口管理、群聊支持 |
| **微信** | PrintWindow 截图 + PaddleOCR 文字识别 | 消息读取（发送暂 stub） |

QQNT 架构暴露了完整的 UIA 树，因此 QQ 适配器不需要 OCR，直接从控件读取消息。微信几乎没有可用 UIA 控件，采用截图 + 本地 OCR 引擎方案。

## 安装

从 [Releases](../../releases) 下载最新 `.msi` 安装包，双击安装。

支持自定义安装路径，安装后在开始菜单和桌面创建快捷方式。

## 使用

1. 启动应用，在控制面板选择聊天应用（QQ / 微信）
   1. QQ：选择窗口隐藏方式（自动 / 副屏 / 贴边）
   2. 微信：配置截图裁剪区域，独立聊天窗口模式需要双击联系人出现单独窗口再用
2. 点击「开始监听」
3. 悬浮窗自动弹出，新消息实时显示
4. 在悬浮窗底部输入框输入回复内容，Enter 发送

### QQ 窗口管理

启动监听后 QQ 窗口会被移到副屏或屏幕边缘，停止监听后恢复原位。窗口自动置顶避免被全屏应用覆盖。

### 微信独立窗口模式

勾选「独立聊天窗口」后输入联系人名称，适配器会按窗口标题查找对应的独立聊天窗口进行截图识别。

## 配置

所有配置保存在 `%APPDATA%/HideYourChat/settings.json`：

| 配置项 | 说明 |
|---|---|
| `SelectedApp` | 聊天应用：`QQ` / `微信` |
| `BackgroundOpacity` | 悬浮窗背景透明度（0.01–1.0） |
| `TextOpacity` | 文字透明度（0.01–1.0） |
| `QQHideModeIndex` | QQ 窗口隐藏方式（0=自动, 1=副屏, 2=贴边） |
| `WeChatCropLeft/Right/Top/Bottom` | 微信截图裁剪比例 |
| `SkippedVersion` | 跳过的更新版本号 |

## 构建

```bash
# 构建解决方案
dotnet build HideYourChat.sln

# 运行
dotnet run --project src/HideYourChat.App

# 测试
dotnet test
```

### 本地打包

```bash
dotnet publish src/HideYourChat.App/HideYourChat.App.csproj -c Release -r win-x64 --self-contained false -o publish/ -p:Version=1.0.0

powershell ./installer/generate-components.ps1 -PublishDir publish -OutputFile installer/PublishedComponents.wxs

wix build -ext WixToolset.UI.wixext installer/HideYourChat.Setup.wxs installer/PublishedComponents.wxs -d "Version=1.0.0" -o output/HideYourChat-1.0.0.msi
```

### 发版

推送 `v*` 标签时 GitHub Actions 自动构建 MSI：

```bash
# 1. 编辑 CHANGELOG.md 添加版本记录
# 2. 编辑 src/HideYourChat.App/HideYourChat.App.csproj 修改 <Version>
# 3. 提交 → 打标签 → 推送
git add .
git commit -m "v1.0.0 发布"
git tag v1.0.0
git push origin main
git push origin v1.0.0
```

## 技术栈

- **.NET 9** WPF (Windows 10 19041+)
- **WPF-UI** v4 — Fluent Design 控件库
- **FlaUI** UIA3 — QQ 消息自动化读写
- **PaddleOCR** + OpenCV — 微信消息 OCR 识别
- **WiX v4** — MSI 安装包构建
- **Serilog** — 日志（每日滚动，保留 7 天）

## 项目结构

```
src/HideYourChat.App/
├── Adapters/           # 聊天应用适配器（QQ / 微信 / Mock）
├── Automation/         # Win32 P/Invoke 封装
├── Core/               # 核心抽象（消息模型、轮询服务、配置）
├── Imaging/            # OCR 引擎、截图、图像处理
├── Overlay/            # 悬浮消息窗
├── Update/             # 自动更新（GitHub Releases）
└── MainWindow.*        # 控制面板主窗口
```

## 许可证

[Apache License 2.0](LICENSE)
