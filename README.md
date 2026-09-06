# IP 切换工具

Windows 单文件小工具：图形界面管理电脑网卡的内网 IP，并支持检测/更换公网 IP。

![PowerShell](https://img.shields.io/badge/PowerShell-5.1%2B-blue) ![Platform](https://img.shields.io/badge/Windows-10%20%2F%2011-lightgrey)

## 功能

- **静态 IP / DHCP 一键切换**：选择网卡后即可设置静态 IP、子网掩码、网关、DNS（支持双 DNS），或一键切回 DHCP 自动获取
- **公网 IP 检测**：显示当前公网 IPv4（自动使用国内检测源，多源兜底）
- **一键重拨换公网 IP**（小米路由器）：自动登录路由器管理接口，读取其保存的 PPPoE 拨号账号并触发重新拨号，等待新公网 IP 生效并显示"旧 IP → 新 IP"
- 带 IP/掩码格式校验、操作确认弹窗和操作日志；修改内网 IP 时自动请求管理员权限（UAC）

## 使用方法

1. 双击 `IP切换工具.bat`（需要和 `ip_tool.ps1` 放在同一个文件夹）
2. 弹出 UAC 提示点"是"（改内网 IP 需要管理员权限；公网 IP 检测/重拨功能不需要）
3. 顶部选择网卡，"当前配置"框会显示该网卡现状

**设置静态 IP**：填入 IP、子网掩码（默认 255.255.255.0）、网关、DNS → 点「应用静态 IP」。网关和 DNS 留空表示不改动。改坏了随时点「切换为 DHCP 自动获取」恢复。

**更换公网 IP（小米路由器）**：在"路由器密码"框填入路由器管理密码 → 点「重拨换新 IP」。工具全程不会看到或保存你的宽带（PPPoE）账号密码，只使用路由器自己存储的那份。

> 重拨会让网络中断约 10~30 秒；可能抽到和之前相同的 IP，再点一次即可。

## 密码安全

- 勾选"记住密码"后，路由器管理密码使用 Windows DPAPI 加密存储在本机 `%LOCALAPPDATA%\MiRouterCred.xml`，仅当前 Windows 账户可解密，不上传、不进仓库
- 不勾选则每次手动输入，内存中使用完即弃

## 实现说明

- 内网 IP 修改：调用 Windows 自带 `netsh`
- 公网 IP 检测：`ip.3322.net` / `members.3322.org` / `api.ipify.org` 依次尝试
- 小米路由器重拨：调用小米未公开的本地管理接口（登录算法从路由器前端 JS 逆向确认），参考了社区项目
  [azwhikaru/xiaomi_router_api_document](https://github.com/azwhikaru/xiaomi_router_api_document)、
  [scientific hackers/pymiwifi](https://github.com/scientifichackers/pymiwifi)
- 仅需 Windows 自带 PowerShell 5.1，无任何第三方依赖；重拨功能在小米 R3600（ROM 1.1.25）上验证通过，其他型号/固件版本未测试，接口如有变化欢迎反馈

## 许可

MIT
