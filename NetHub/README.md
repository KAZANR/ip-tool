# NetHub 网枢

全新的 Windows 桌面网络工具（.NET 10 · WPF）：管理网卡静态 IP / DHCP，检测公网 IP，支持小米路由器一键 PPPoE 重拨换 IP。

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4) ![UI](https://img.shields.io/badge/UI-WPF-lightgrey) ![Version](https://img.shields.io/badge/version-1.0.0-3B82F6)

<div align="center">

<img src="Assets/NetHub.png" width="120" alt="NetHub 图标">

**NetHub · 网枢** — 现代化 IP 工作台

</div>

## 与旧版关系

这是对原 PowerShell IP 切换工具的**完全重写**：

| | 旧 IPilot / ip_tool | 新 NetHub |
|--|---------------------|-----------|
| 技术栈 | PowerShell + WPF 脚本 | .NET 10 C# WPF 正式工程 |
| 架构 | 单文件脚本 | 服务分层（Network / Router / Credentials） |
| 界面 | 浅色三栏（方案 B） | 同风格，独立 XAML 资源 |
| 发布 | 依赖本机 PowerShell | 编译为 `NetHub.exe` |

功能全部保留并落在独立服务类中。

## 功能

- **适配器列表与实时信息**：状态、速率、IPv4、DHCP/静态、网关、DNS  
- **静态 IP 配置**：IP / 掩码 / 网关 / 双 DNS，格式校验与确认  
- **一键 DHCP**：地址与 DNS 一并切回自动获取  
- **填入当前 IP**：从现网配置回填表单  
- **公网 IP 检测**：多源兜底  
- **小米路由器重拨**：登录 LuCI → 读 PPPoE → 触发重拨 → 等待新 IP  
- **密码安全**：DPAPI 本机加密；兼容读取旧 `IPilotRouterCred.xml`  
- **权限**：清单声明 `requireAdministrator`，启动即提权  

## 目录结构

```
NetHub/
├─ NetHub.csproj
├─ app.manifest              # 要求管理员
├─ App.xaml / App.xaml.cs
├─ MainWindow.xaml(.cs)      # 浅色现代 UI
├─ Models/                   # 数据模型
├─ Services/
│  ├─ NetworkService.cs      # 网卡 / netsh / 公网 IP
│  └─ RouterService.cs       # 小米路由 + DPAPI 凭据
├─ Assets/
│  ├─ NetHub.ico
│  └─ NetHub.png
├─ NetHub.bat                # 启动已编译 EXE
└─ bin/Debug/net10.0-windows/NetHub.exe
```

## 构建与运行

```powershell
cd NetHub
dotnet build -c Release
# 或直接运行已生成的
.\bin\Debug\net10.0-windows\NetHub.exe
```

若本机 NuGet 恢复异常，可用当前仓库已生成的 `bin\Debug` 产物直接运行；或在正常网络环境重新 `dotnet restore && dotnet build`。

也可双击 `NetHub.bat`（指向 Debug 输出）。

## 界面说明（浅色简洁 · 方案 B）

- 页面底 `#EEF1F6`，白卡片，主色蓝 `#3B82F6`  
- 左：适配器选择 + 信息卡  
- 中：静态 / DHCP 开关 + IP 表单  
- 右：公共 IP、路由器重拨、活动日志  

## 安全说明

- 不保存宽带（PPPoE）账号密码，只使用路由器侧已保存凭据触发重拨  
- 路由器管理密码经 Windows DPAPI（`CryptProtectData`）绑定当前用户  
- 重拨会导致约 10–30 秒断网，可能抽到相同公网 IP  

## 许可

MIT
