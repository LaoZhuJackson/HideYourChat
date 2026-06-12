## v1.0.2

### 🚀 新增
- 检查更新功能，支持从 GitHub Releases 获取最新版本
- 更新通知弹窗，展示版本号、文件大小、更新内容
- 强制更新模式，release body 中包含 `[force]` 时不可跳过
- 代理和 GitHub Token 配置支持

### 🔧 修复
- 修复 CI release workflow 中 WiX 扩展不兼容的问题
- 修复版本号硬编码 1.0.0，改为读取程序集版本
- 修复 WPF-UI ThemeResource 枚举值报错

### 📦 打包
- WiX v4 构建 MSI 安装包
- 支持自定义安装路径

## v1.0.1

### 🔧 修复
- 修复 CI 工作流中 WiX 版本和扩展兼容性

## v1.0.0

### 🎉 首次发布
- 微信 OCR 消息监听
- QQ UIA 消息监听
- 半透明悬浮窗快速回复