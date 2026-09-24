# Network Doctor

Network Doctor 是一个面向 Windows 10/11 的现代化网络诊断与修复工具。桌面端使用 EUI-NEO C++ 原生界面，核心网络操作由 .NET 8 服务和固定命令 CLI 完成。

## 功能

- 检测 Windows 系统代理、WinHTTP 和代理环境变量
- 一键备份并关闭遗留代理设置
- 撤销代理修复并验证 HTTPS 网络连接
- 查看网卡状态、IPv4、IPv6、网关、DNS 和链路速率
- 检测公网 IPv4 / IPv6
- 配置静态 IPv4、切换 DHCP、填入当前网卡配置
- 保存小米路由器管理密码（Windows DPAPI）
- 读取路由器 PPPoE 信息并执行确认后的重拨

## 运行已发布版本

```text
publish-neo/NetworkDoctorNeo.exe
```

启动后，代理诊断、网卡工具和路由器页面分别位于左侧导航。

## 构建

### 必要环境

- Windows 10/11 x64
- .NET 8 SDK 或兼容 SDK
- Visual Studio 2022 Build Tools：安装 C++ 桌面开发工作负载
- CMake 3.14+
- Git

EUI-NEO 依赖通过 CMake `FetchContent` 自动获取，默认使用 AtomGit 镜像和 Gitee 的 JSON 镜像。

### 构建并发布 EUI-NEO 版本

在仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-neo.ps1
```

脚本会构建 EUI-NEO 原生应用和自包含 CLI，并输出到 `publish-neo/`。

## 测试

```powershell
dotnet test .\NetworkDoctor.slnx --no-restore
ctest --test-dir .\native\build -C Release --output-on-failure
```

## CLI 后端

CLI 只接受固定命令，输出 JSON，供 EUI-NEO 调用：

```text
inspect
repair
restore <backup-file>
verify
adapters
public-ip
ip-static <adapter> <ip> <mask> <gateway> <dns1> <dns2>
ip-dhcp <adapter>
router-save <host>
redial <host>
```

`router-save` 和 `redial` 的密码只通过标准输入传递，不进入命令行、日志或 JSON 响应。路由器密码使用当前 Windows 用户的 DPAPI 加密保存。

## 安全说明

- 所有数据在本机处理，不上传网络信息。
- 代理修复、静态 IP、DHCP 和 PPPoE 重拨执行前都需要确认。
- 错误的网关配置可能导致断网；PPPoE 重拨期间网络会短暂中断。
- 不会把 `NO_PROXY` 误判为活动代理，也不会自动清除它。
- `bin/`、`obj/`、`native/build/` 和 `publish/` 等构建目录不会纳入源代码仓库。

## 项目结构

```text
src/NetworkDoctor.Core       核心代理诊断模型
src/NetworkDoctor.Windows   Windows 网络、代理、路由和 DPAPI 服务
src/NetworkDoctor.Cli       固定命令 JSON CLI
src/NetworkDoctor.App       旧版 WPF 回退界面
native/                      EUI-NEO C++ 原生界面
tests/                       C# 测试
scripts/publish-neo.ps1      EUI-NEO 发布脚本
```

当前仓库保留 `src/NetworkDoctor.App` 作为旧版 WPF 回退版本；日常使用和发布入口是 `NetworkDoctorNeo.exe`。
