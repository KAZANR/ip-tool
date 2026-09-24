# NetHub 功能合并实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 subagent-driven-development（推荐）或 executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 将 NetHub 的网卡管理、静态 IP/DHCP、公网 IP 检测和小米路由器 PPPoE 重拨能力合并到 Network Doctor 的 EUI-NEO 桌面程序。

**架构：** 复用 NetHub 的网络服务逻辑和路由器协议逻辑，但迁移为 `NetworkDoctor.Windows` 服务；EUI-NEO 继续作为唯一桌面壳，通过固定 CLI JSON 命令调用服务。路由器密码只通过 stdin 传给 CLI 并使用 DPAPI 保存，不进入命令行参数。

**技术栈：** C#/.NET 8、C++17、EUI-NEO、CLI JSON、WinHTTP/NetworkInformation、DPAPI、xUnit。

**规格：** NetHub MIT 功能全部合并；现有代理诊断、备份、撤销、联网验证不得回归；所有系统修改继续确认后执行。

## 全局约束

- Windows 10/11 x64。
- 固定命令白名单，不接受用户输入拼接命令行。
- 路由器密码不得出现在命令行、日志或 JSON 响应中。
- 静态 IP、DHCP、PPPoE 重拨必须经过用户确认。
- 复用现有 C# 26 个测试基线，并新增合并功能测试。
- 不添加代码注释。

---

### 任务 1：迁移网卡与公网 IP 服务

**文件：**
- 创建：`src/NetworkDoctor.Windows/NetworkManagementService.cs`
- 创建：`src/NetworkDoctor.Windows/NetworkModels.cs`
- 测试：`tests/NetworkDoctor.Tests/NetworkManagementServiceTests.cs`

- [ ] 编写失败测试：适配器过滤、IPv4/IPv6/网关/DNS 解析、静态 IP 校验、DHCP/静态命令参数生成。
- [ ] 运行 `dotnet test` 确认因服务缺失而失败。
- [ ] 迁移 NetHub 的 `NetworkService` 纯逻辑，命令执行改为 `ProcessStartInfo.ArgumentList`。
- [ ] 实现 `ListAdapters`、`GetDetail`、`ApplyStatic`、`ApplyDhcp`、`GetPublicIps`。
- [ ] 运行完整 C# 测试并确认无回归。

### 任务 2：迁移路由器与 DPAPI 服务

**文件：**
- 创建：`src/NetworkDoctor.Windows/MiRouterService.cs`
- 创建：`src/NetworkDoctor.Windows/CredentialStore.cs`
- 测试：`tests/NetworkDoctor.Tests/RouterServiceTests.cs`

- [ ] 编写失败测试：凭据保存/读取使用 DPAPI、路由器登录请求、PPPoE 重拨命令构造。
- [ ] 运行测试确认失败。
- [ ] 迁移 `MiRouterClient` 和 `CredentialStore`，密码只从 stdin 接收。
- [ ] 实现 `redial <host>` 固定命令，读取 DPAPI 密码并在失败时返回安全错误。
- [ ] 运行完整 C# 测试。

### 任务 3：扩展 CLI JSON 合同

**文件：**
- 修改：`src/NetworkDoctor.Cli/Program.cs`
- 修改：`src/NetworkDoctor.Cli/NetworkDoctor.Cli.csproj`
- 测试：`tests/NetworkDoctor.Tests/CliContractTests.cs`

- [ ] 编写失败测试：固定命令分类、JSON 字段、密码不出现在响应、非法网卡参数拒绝。
- [ ] 运行测试确认失败。
- [ ] 增加 `adapters`、`public-ip`、`ip-static`、`ip-dhcp`、`redial` 命令。
- [ ] `redial` 密码从 stdin 读取；CLI 不回显密码。
- [ ] 运行 CLI 合同测试和完整测试。

### 任务 4：接入 EUI-NEO 页面

**文件：**
- 修改：`native/app_state.h`
- 修改：`native/app_state.cpp`
- 修改：`native/cli_client.h`
- 修改：`native/cli_client.cpp`
- 修改：`native/main.cpp`

- [ ] 扩展页面状态：网卡列表、选中网卡、公网 IP、静态 IP 表单、路由器状态、错误消息。
- [ ] 扩展 CLI 客户端命令枚举和 JSON 解析。
- [ ] 增加“网卡工具”页面：适配器选择、状态、IPv4/IPv6、网关、DNS、公网 IP、静态/DHCP 表单。
- [ ] 增加“路由器”页面：路由器地址、密码输入、保存密码、PPPoE 重拨按钮和结果提示。
- [ ] 使用 EUI-NEO input/card/button/dialog 组件，保持当前紧凑布局。
- [ ] 启动时只自动检测代理；其他网络功能按按钮触发。

### 任务 5：发布与验收

- [ ] 运行 `dotnet test NetworkDoctor.slnx --no-restore`。
- [ ] 运行 EUI-NEO Release 构建。
- [ ] 运行 CLI `adapters`、`public-ip` 和安全的读取命令。
- [ ] 启动原生程序并截图检查页面无越界、重叠、裁切。
- [ ] 不在验收中执行真实静态 IP、DHCP 或 PPPoE 重拨，除非用户单独确认。
- [ ] 发布到 `publish-neo` 并打开最终程序。
