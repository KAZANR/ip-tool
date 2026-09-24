# EUI-NEO 原生界面迁移实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 subagent-driven-development（推荐）或 executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 将 Network Doctor 的桌面界面从 WPF 迁移为 EUI-NEO 原生应用，同时保留现有 C# 代理检测、修复、备份和联网验证逻辑。

**架构：** 保留 `NetworkDoctor.Core` 与 `NetworkDoctor.Windows` 作为唯一业务核心，新增 `NetworkDoctor.Cli` 作为固定命令 JSON 后端；EUI-NEO C++ 应用只负责界面、状态和调用固定 CLI，不直接修改系统代理。WPF 项目保留作为旧版备份，不再作为主发布入口。

**技术栈：** C#/.NET 8、C++17、CMake 3.14+、EUI-NEO `main` 分支、GLFW/OpenGL、Windows 10/11。

**规格：** 用户已确认真正迁移到 EUI-NEO；视觉目标为浅色侧边导航、白色圆角内容卡片、蓝色主色、现代简约桌面工具界面。

## 全局约束

- Windows 10/11 x64。
- 代理修复必须先备份、先确认，只允许固定白名单操作。
- `NO_PROXY` 不得被当作活动代理，不得被自动清除。
- EUI-NEO 业务层不得直接访问注册表、环境变量或任意命令。
- C++ 侧只允许调用固定 CLI 参数，不接受用户输入拼接命令。
- 不新增代码注释。
- 保留现有 20 个 C# 测试，并在迁移后继续通过。

---

### 任务 1：创建 C# CLI 后端

**文件：**
- 创建：`src/NetworkDoctor.Cli/NetworkDoctor.Cli.csproj`
- 创建：`src/NetworkDoctor.Cli/Program.cs`
- 修改：`NetworkDoctor.slnx`
- 测试：`tests/NetworkDoctor.Tests/CliContractTests.cs`

- [ ] **步骤 1：编写失败的 CLI 合同测试**

测试 `inspect` 返回固定 JSON 字段 `ok`、`findings`、`score`、`summary`；测试 `repair`、`restore`、`verify` 只接受固定命令名；非法参数返回非零退出码且不执行系统修改。

- [ ] **步骤 2：运行测试验证失败**

运行：`dotnet test tests/NetworkDoctor.Tests/NetworkDoctor.Tests.csproj --no-restore`

预期：因 `NetworkDoctor.Cli` 尚不存在而失败。

- [ ] **步骤 3：实现 CLI 合同**

实现 `Program.Main(string[] args)`，固定支持：

```text
inspect
repair
restore <backup-file>
verify
```

使用 `WindowsProxyInspector`、`WindowsProxyRepairer`、`WindowsConnectivityVerifier`，将结果序列化为 UTF-8 JSON；`repair` 将备份写入传入路径或 LocalAppData；`restore` 只读取 JSON 备份，不执行任意文件内容；错误写 stderr 并返回 1。

- [ ] **步骤 4：运行测试验证通过**

运行：`dotnet test tests/NetworkDoctor.Tests/NetworkDoctor.Tests.csproj --no-restore`

预期：全部测试通过。

---

### 任务 2：建立 EUI-NEO C++ 应用壳

**文件：**
- 创建：`native/CMakeLists.txt`
- 创建：`native/main.cpp`
- 创建：`native/app_state.h`
- 创建：`native/app_state.cpp`
- 创建：`native/cli_client.h`
- 创建：`native/cli_client.cpp`
- 创建：`native/ui_theme.h`
- 创建：`native/ui_theme.cpp`

- [ ] **步骤 1：配置 FetchContent**

使用 `FetchContent` 获取 `https://github.com/sudoevolve/EUI-NEO.git` 的 `main` 分支；使用 `add_executable(NetworkDoctorNeo main.cpp ...)` 和 `eui_neo_configure_app(NetworkDoctorNeo)`，不直接引用 `core/`。

- [ ] **步骤 2：建立固定 CLI 调用接口**

`cli_client` 只提供 `run(command)`，命令枚举固定为 `inspect`、`repair`、`verify`、`restore`；使用 `CreateProcessW`/标准输入输出和固定参数数组，不把用户输入拼接到命令行；解析 JSON 字段到 `AppState`。

- [ ] **步骤 3：建立页面状态**

`AppState` 持有当前页面、检查状态、分数、发现项、修复结果、备份路径、错误消息和确认弹窗状态；状态改变后调用 `app::requestUpdate()`。

- [ ] **步骤 4：配置 Windows 依赖**

CMake 链接 EUI-NEO 所需 Windows 库和 shell32；默认 GLFW + OpenGL；设置窗口标题 `Network Doctor`、窗口尺寸 `1180x780`、关闭 Debug 标题统计。

---

### 任务 3：实现 EUI-NEO 现代化界面

**文件：**
- 修改：`native/main.cpp`
- 修改：`native/ui_theme.cpp`
- 修改：`native/ui_theme.h`

- [ ] **步骤 1：实现应用根布局**

使用 EUI-NEO DSL 实现左侧 250px 导航栏与右侧主内容区；主内容为浅灰画布、白色圆角卡片、蓝色标题和蓝色主按钮。

- [ ] **步骤 2：实现导航与内容页面**

导航项固定为“检测与修复”“使用说明”“关于工具”；当前页面用蓝色填充项表示。检测页显示健康状态、分数、代理来源列表和操作按钮；说明页显示小白操作流程；关于页显示安全边界和版本。

- [ ] **步骤 3：实现交互状态**

开始检测按钮设置 busy 状态并调用 `inspect`；关闭代理按钮先打开 EUI-NEO `dialog` 确认框，确认后调用 `repair`；撤销按钮调用 `restore`；联网验证调用 `verify`；所有按钮使用 `.disabled()` 和 transition 状态。

- [ ] **步骤 4：实现结果视觉**

使用 `components::card`、`components::button`、`components::dialog`、`components::toast`、`components::progress`、`components::text`；状态颜色固定为蓝、绿、橙、红，避免依赖外部主题。

---

### 任务 4：替换发布入口并保留旧版回退

**文件：**
- 修改：`NetworkDoctor.slnx`
- 修改：`.gitignore`
- 创建：`scripts/publish-neo.ps1`
- 修改：`src/NetworkDoctor.App/NetworkDoctor.App.csproj`

- [ ] **步骤 1：保留 WPF 回退但不作为主入口**

WPF 项目继续可构建；发布目录只将 EUI-NEO 生成的 `NetworkDoctorNeo.exe` 作为用户入口，旧版 WPF 发布目录保留在 `publish-wpf` 供回滚。

- [ ] **步骤 2：添加发布脚本**

脚本检查 CMake、Visual Studio 编译器、EUI-NEO 构建目录和 CLI 产物；配置 `native/build`；执行 `cmake --build native/build --config Release`；将 EUI-NEO exe、CLI exe 和必要运行时复制到 `publish`。

- [ ] **步骤 3：更新忽略规则**

忽略 `native/build`、`native/_deps`、`publish` 和 CLI 发布输出；不忽略源代码、CMakeLists 和测试。

---

### 任务 5：迁移验证与发布

**文件：**
- 创建：`tests/NetworkDoctor.Tests/CliContractTests.cs`
- 创建：`native/README`（仅在用户要求文档时创建）

- [ ] **步骤 1：运行 C# 全量测试**

运行：`dotnet test NetworkDoctor.slnx --no-restore`

预期：原有 20 个测试与新增 CLI 合同测试全部通过。

- [ ] **步骤 2：配置 EUI-NEO**

运行：`cmake -S native -B native/build -DEUI_ENABLE_TRAY=OFF`

预期：EUI-NEO FetchContent 成功，生成 Visual Studio 工程。

- [ ] **步骤 3：构建原生应用**

运行：`cmake --build native/build --config Release --parallel`

预期：生成 `native/build/Release/NetworkDoctorNeo.exe`，无编译错误。

- [ ] **步骤 4：执行桌面冒烟检查**

启动原生应用 3 秒，确认进程存活；检查 CLI `inspect` 输出 JSON；不使用真实 `repair` 命令破坏当前网络设置。

- [ ] **步骤 5：记录环境阻塞**

如果本机没有 CMake、Visual Studio Build Tools 或 EUI-NEO 依赖，保留源码和构建脚本，明确报告缺少的工具，不伪称原生应用已编译。
