#include "eui_neo.h"

#include "app_state.h"
#include "cli_client.h"
#include "ui_theme.h"

#include <algorithm>
#include <array>
#include <functional>
#include <string>
#include <utility>
#include <vector>

namespace app {
namespace {

constexpr float kNavigationWidth = 220.0f;
constexpr float kPagePadding = 28.0f;
constexpr const char* kTaskKey = "networkdoctor.cli";

networkdoctor::AppState& state() {
    return networkdoctor::AppState::instance();
}

networkdoctor::CliClient& client() {
    static networkdoctor::CliClient instance(networkdoctor::CliClient::resolveExecutablePath());
    return instance;
}

networkdoctor::UiTheme theme() {
    return networkdoctor::lightUiTheme();
}

void completeOperation(
    networkdoctor::CliCommand command,
    const app::async::Result<networkdoctor::CliResult>& result) {
    if (result.ok) {
        state().applyResult(command, result.value);
    } else if (!result.value.output.empty()) {
        state().applyResult(command, result.value);
    } else {
        state().failOperation(result.error.empty() ? "CLI 后台任务失败" : result.error);
    }
}

bool startRequest(networkdoctor::CliRequest request) {
    if (!client().isAvailable()) {
        state().failOperation("未找到 NetworkDoctor.Cli.exe，请检查安装目录或 NETWORKDOCTOR_CLI");
        return false;
    }
    if (request.command == networkdoctor::CliCommand::Restore &&
        !networkdoctor::CliClient::isExpectedBackupPath(request.backupPath)) {
        state().failOperation("备份路径无效，已拒绝执行撤销");
        return false;
    }
    if (!state().beginOperation(request.command)) {
        return false;
    }

    const networkdoctor::CliCommand command = request.command;
    const bool started = app::async::restart(
        kTaskKey,
        [request = std::move(request)]() mutable {
            networkdoctor::CliResult result = client().run(request);
            std::fill(request.stdinText.begin(), request.stdinText.end(), '\0');
            request.stdinText.clear();
            return result;
        },
        [command](const app::async::Result<networkdoctor::CliResult>& result) {
            completeOperation(command, result);
        });

    if (!started) {
        state().failOperation("无法启动 CLI 后台任务");
    }
    return started;
}

bool startOperation(networkdoctor::CliCommand command) {
    networkdoctor::CliRequest request;
    request.command = command;
    if (command == networkdoctor::CliCommand::Restore) {
        request.backupPath = state().backupPath;
    }
    return startRequest(std::move(request));
}

bool startRouterOperation(networkdoctor::CliCommand command, std::string_view password) {
    if (!networkdoctor::CliClient::isRouterHost(state().routerHost)) {
        state().failOperation("路由器地址只能是无路径的 IPv4 或 DNS 名称");
        return false;
    }
    if (command == networkdoctor::CliCommand::RouterSave && password.empty()) {
        state().failOperation("路由器密码不能为空");
        return false;
    }
    networkdoctor::CliRequest request;
    request.command = command;
    request.routerHost = state().routerHost;
    request.stdinText = password;
    return startRequest(std::move(request));
}

void startInitialInspect() {
    static bool scheduled = false;
    if (scheduled) {
        return;
    }

    if (!client().isAvailable()) {
        return;
    }
    if (!state().beginOperation(networkdoctor::CliCommand::Inspect)) {
        return;
    }
    scheduled = true;

    const bool started = app::async::restart(
        kTaskKey,
        [] { return client().run(networkdoctor::CliCommand::Inspect); },
        [](const app::async::Result<networkdoctor::CliResult>& result) {
            completeOperation(networkdoctor::CliCommand::Inspect, result);
        });
    if (!started) {
        scheduled = false;
        state().failOperation("无法启动首次检测");
    }
}

void navigationButton(
    eui::Ui& ui,
    const std::string& id,
    const std::string& text,
    float y,
    networkdoctor::Page target) {
    const networkdoctor::UiTheme uiTheme = theme();
    const bool selected = state().page == target;
    components::button(ui, id)
        .theme(uiTheme.tokens, selected)
        .position(16.0f, y)
        .size(188.0f, 40.0f)
        .radius(10.0f)
        .text(text)
        .fontSize(14.0f)
        .colors(
            selected ? eui::Color("#2563EB") : eui::Color("#FFFFFF"),
            selected ? eui::Color("#1D4ED8") : eui::Color("#EFF4FB"),
            selected ? eui::Color("#1E40AF") : eui::Color("#E4ECF8"))
        .textColor(selected ? eui::Color("#FFFFFF") : eui::Color("#344054"))
        .transition(uiTheme.motion)
        .onClick([&ui, target] {
            if (state().page == networkdoctor::Page::Router && target != networkdoctor::Page::Router) {
                ui.state<std::string>("router.password").clear();
            }
            state().setPage(target);
        })
        .build();
}

void composeNavigation(eui::Ui& ui, float height) {
    const networkdoctor::UiTheme uiTheme = theme();
    ui.stack("navigation")
        .size(kNavigationWidth, height)
        .content([&] {
            ui.rect("navigation.background")
                .size(kNavigationWidth, height)
                .color("#F7F9FC")
                .border(1.0f, eui::Color("#E2E8F0"))
                .build();

            components::text(ui, "navigation.brand", uiTheme.tokens)
                .position(18.0f, 24.0f)
                .size(184.0f, 32.0f)
                .text("Network Doctor")
                .fontSize(18.0f)
                .fontWeight(780)
                .color("#172B4D")
                .build();

            ui.text("navigation.subtitle")
                .position(18.0f, 62.0f)
                .size(184.0f, 20.0f)
                .text("网络代理诊断工具")
                .fontSize(12.0f)
                .color("#667085")
                .build();

            navigationButton(ui, "navigation.diagnose", "检测与修复", 112.0f, networkdoctor::Page::Diagnose);
            navigationButton(ui, "navigation.network", "网卡工具", 160.0f, networkdoctor::Page::NetworkTools);
            navigationButton(ui, "navigation.router", "路由器", 208.0f, networkdoctor::Page::Router);
            navigationButton(ui, "navigation.guide", "使用说明", 256.0f, networkdoctor::Page::Guide);
            navigationButton(ui, "navigation.about", "关于工具", 304.0f, networkdoctor::Page::About);

            ui.text("navigation.safety")
                .position(18.0f, height - 74.0f)
                .size(184.0f, 48.0f)
                .text("仅执行固定命令\n所有系统修改均可撤销")
                .fontSize(12.0f)
                .lineHeight(20.0f)
                .wrap(true)
                .color("#667085")
                .build();
        })
        .build();
}

void composePageTitle(
    eui::Ui& ui,
    float x,
    float width,
    const std::string& title,
    const std::string& subtitle) {
    ui.text("page.title")
        .position(x, 24.0f)
        .size(width, 34.0f)
        .text(title)
        .fontSize(26.0f)
        .fontWeight(800)
        .color("#172B4D")
        .build();

    ui.text("page.subtitle")
        .position(x, 62.0f)
        .size(width, 22.0f)
        .text(subtitle)
        .fontSize(13.0f)
        .color("#667085")
        .build();
}

std::string healthTitle() {
    if (state().busy) {
        return "正在检查网络配置";
    }
    if (state().score >= 90) {
        return "网络状态良好";
    }
    if (state().score >= 60) {
        return "发现可优化项目";
    }
    return "发现活动代理风险";
}

std::string healthColor() {
    if (state().score >= 90) {
        return "#16A34A";
    }
    if (state().score >= 60) {
        return "#D97706";
    }
    return "#DC2626";
}

void composeHealth(eui::Ui& ui, float x, float y, float width) {
    ui.rect("diagnosis.health.background")
        .position(x, y)
        .size(width, 126.0f)
        .color("#F4F8FF")
        .radius(16.0f)
        .border(1.0f, eui::Color("#D6E4FF"))
        .build();

    components::text(ui, "diagnosis.health.title", theme().tokens)
        .position(x + 16.0f, y + 14.0f)
        .size(width * 0.55f, 24.0f)
        .text("网络健康")
        .fontSize(16.0f)
        .fontWeight(760)
        .color("#172B4D")
        .build();

    ui.text("diagnosis.health.state")
        .position(x + 16.0f, y + 43.0f)
        .size(width * 0.55f, 22.0f)
        .text(healthTitle())
        .fontSize(14.0f)
        .color(healthColor())
        .build();

    ui.text("diagnosis.health.summary")
        .position(x + 16.0f, y + 70.0f)
        .size(width * 0.58f, 24.0f)
        .text(state().errorMessage.empty() ? state().summary : state().errorMessage)
        .fontSize(12.0f)
        .wrap(true)
        .color("#667085")
        .build();

    if (state().verificationPassed.has_value()) {
        ui.text("diagnosis.verify.result")
            .position(x + 16.0f, y + 101.0f)
            .size(width * 0.58f, 18.0f)
            .text(*state().verificationPassed ? "联网验证通过" : "联网验证失败")
            .fontSize(12.0f)
            .color(*state().verificationPassed ? "#16A34A" : "#DC2626")
            .build();
    }

    components::text(ui, "diagnosis.health.score", theme().tokens)
        .position(x + width * 0.68f, y + 14.0f)
        .size(width * 0.25f, 36.0f)
        .text(state().busy && state().operation == networkdoctor::CliCommand::Inspect ? "--" : std::to_string(state().score))
        .fontSize(26.0f)
        .fontWeight(820)
        .horizontalAlign(eui::HorizontalAlign::Right)
        .color(healthColor())
        .build();

    ui.stack("diagnosis.health.progress.host")
        .position(x + width * 0.58f, y + 70.0f)
        .size(width * 0.34f, 10.0f)
        .content([&] {
            components::progress(ui, "diagnosis.health.progress")
                .theme(theme().tokens)
                .size(width * 0.34f, 10.0f)
                .value(state().busy ? 0.55f : static_cast<float>(std::clamp(state().score, 0, 100)) / 100.0f)
                .build();
        })
        .build();
}

void composeFindings(eui::Ui& ui, float x, float y, float width) {
    components::text(ui, "diagnosis.findings.title", theme().tokens)
        .position(x, y)
        .size(width, 24.0f)
        .text("代理发现")
        .fontSize(16.0f)
        .fontWeight(760)
        .color("#172B4D")
        .build();

    if (state().findings.empty()) {
        ui.rect("diagnosis.findings.empty")
            .position(x, y + 32.0f)
            .size(width, 64.0f)
            .color("#F8FAFC")
            .radius(14.0f)
            .border(1.0f, eui::Color("#E2E8F0"))
            .build();
        ui.text("diagnosis.findings.empty.text")
            .position(x + 16.0f, y + 51.0f)
            .size(width - 32.0f, 24.0f)
            .text("未发现活动代理设置，网络配置看起来正常")
            .fontSize(13.0f)
            .color("#667085")
            .build();
        return;
    }

    const std::size_t visibleCount = std::min<std::size_t>(state().findings.size(), 3);
    const float rowHeight = 58.0f;
    for (std::size_t index = 0; index < visibleCount; ++index) {
        const networkdoctor::ProxyFinding& finding = state().findings[index];
        const float rowY = y + 32.0f + static_cast<float>(index) * (rowHeight + 8.0f);
        ui.rect("diagnosis.finding." + std::to_string(index))
            .position(x, rowY)
            .size(width, rowHeight)
            .color("#FAFBFC")
            .radius(14.0f)
            .border(1.0f, eui::Color("#E2E8F0"))
            .build();

        components::text(ui, "diagnosis.finding.source." + std::to_string(index), theme().tokens)
            .position(x + 14.0f, rowY + 9.0f)
            .size(142.0f, 20.0f)
            .text(finding.source + " · " + finding.scope)
            .fontSize(12.0f)
            .fontWeight(720)
            .color("#2563EB")
            .build();

        components::text(ui, "diagnosis.finding.name." + std::to_string(index), theme().tokens)
            .position(x + 14.0f, rowY + 32.0f)
            .size(width * 0.34f, 18.0f)
            .text(finding.name)
            .fontSize(12.0f)
            .color("#344054")
            .build();

        ui.text("diagnosis.finding.address." + std::to_string(index))
            .position(x + width * 0.36f, rowY + 9.0f)
            .size(width * 0.62f, 20.0f)
            .text(finding.address)
            .fontSize(12.0f)
            .color("#344054")
            .build();

        ui.text("diagnosis.finding.impact." + std::to_string(index))
            .position(x + width * 0.36f, rowY + 32.0f)
            .size(width * 0.62f, 18.0f)
            .text(finding.impact)
            .fontSize(11.0f)
            .color("#667085")
            .build();
    }

    if (state().findings.size() > visibleCount) {
        ui.text("diagnosis.findings.more")
            .position(x + 4.0f, y + 32.0f + static_cast<float>(visibleCount) * (rowHeight + 8.0f))
            .size(width - 8.0f, 18.0f)
            .text("另有 " + std::to_string(state().findings.size() - visibleCount) + " 个代理来源")
            .fontSize(11.0f)
            .color("#667085")
            .build();
    }
}

void composeActionButtons(eui::Ui& ui, float x, float y, float width) {
    const networkdoctor::UiTheme uiTheme = theme();
    const float gap = 8.0f;
    const float buttonWidth = (width - gap * 3.0f) / 4.0f;
    const bool canRepair = !state().findings.empty() && state().backupPath.empty();
    const bool canRestore = !state().backupPath.empty();

    components::button(ui, "diagnosis.inspect")
        .theme(uiTheme.tokens, true)
        .position(x, y)
        .size(buttonWidth, 40.0f)
        .radius(10.0f)
        .text(state().busy && state().operation == networkdoctor::CliCommand::Inspect ? "正在检测" : "开始检测")
        .fontSize(14.0f)
        .disabled(state().busy)
        .transition(uiTheme.motion)
        .onClick([] { startOperation(networkdoctor::CliCommand::Inspect); })
        .build();

    components::button(ui, "diagnosis.repair")
        .theme(uiTheme.tokens, true)
        .position(x + buttonWidth + gap, y)
        .size(buttonWidth, 40.0f)
        .radius(10.0f)
        .text(state().busy && state().operation == networkdoctor::CliCommand::Repair ? "正在关闭" : "关闭代理")
        .fontSize(14.0f)
        .disabled(state().busy || !canRepair)
        .transition(uiTheme.motion)
        .onClick([] { state().setRepairConfirmation(true); })
        .build();

    components::button(ui, "diagnosis.restore")
        .theme(uiTheme.tokens, false)
        .position(x + (buttonWidth + gap) * 2.0f, y)
        .size(buttonWidth, 40.0f)
        .radius(10.0f)
        .text(state().busy && state().operation == networkdoctor::CliCommand::Restore ? "正在撤销" : "撤销")
        .fontSize(14.0f)
        .textColor(eui::Color("#344054"))
        .disabled(state().busy || !canRestore)
        .transition(uiTheme.motion)
        .onClick([] { startOperation(networkdoctor::CliCommand::Restore); })
        .build();

    components::button(ui, "diagnosis.verify")
        .theme(uiTheme.tokens, false)
        .position(x + (buttonWidth + gap) * 3.0f, y)
        .size(buttonWidth, 40.0f)
        .radius(10.0f)
        .text(state().busy && state().operation == networkdoctor::CliCommand::Verify ? "正在验证" : "联网验证")
        .fontSize(14.0f)
        .textColor(eui::Color("#344054"))
        .disabled(state().busy)
        .transition(uiTheme.motion)
        .onClick([] { startOperation(networkdoctor::CliCommand::Verify); })
        .build();
}

void composeDiagnosis(eui::Ui& ui, const eui::Screen& screen) {
    const networkdoctor::UiTheme uiTheme = theme();
    const float canvasX = kNavigationWidth;
    const float canvasWidth = std::max(0.0f, screen.width - canvasX);
    const float contentX = canvasX + kPagePadding;
    const float contentWidth = std::max(0.0f, canvasWidth - kPagePadding * 2.0f);
    const float cardY = 94.0f;
    const float findingsY = cardY + 150.0f;
    const std::size_t visibleCount = std::min<std::size_t>(state().findings.size(), 3);
    const float findingsContentHeight = state().findings.empty()
        ? 100.0f
        : static_cast<float>(visibleCount) * 66.0f + (state().findings.size() > visibleCount ? 20.0f : 0.0f);
    const float actionY = findingsY + findingsContentHeight + 12.0f;
    const float cardHeight = actionY - cardY + 58.0f;

    ui.rect("diagnosis.background")
        .position(canvasX, 0.0f)
        .size(canvasWidth, screen.height)
        .color("#F4F6F8")
        .build();

    composePageTitle(
        ui,
        contentX,
        contentWidth,
        "检测与修复",
        "定位活动代理设置，关闭遗留配置，并验证联网状态。");

    components::card(ui, "diagnosis.card")
        .position(contentX, cardY)
        .size(contentWidth, cardHeight)
        .padding(18.0f)
        .theme(uiTheme.tokens)
        .color(eui::Color("#FFFFFF"))
        .radius(20.0f)
        .border(1.0f, eui::Color("#E2E8F0"))
        .content([] {})
        .build();

    composeHealth(ui, contentX + 18.0f, cardY + 18.0f, contentWidth - 36.0f);
    composeFindings(ui, contentX + 18.0f, findingsY, contentWidth - 36.0f);

    composeActionButtons(ui, contentX + 18.0f, actionY, contentWidth - 36.0f);
}

std::string valueOr(const std::string& value, const char* fallback) {
    return value.empty() ? fallback : value;
}

std::string joinDnsServers(const std::vector<std::string>& servers) {
    std::string result;
    for (const std::string& server : servers) {
        if (!result.empty()) {
            result += "、";
        }
        result += server;
    }
    return result;
}

void composeFieldLabel(eui::Ui& ui, const std::string& id, float x, float y, float width, const std::string& text) {
    ui.text(id)
        .position(x, y)
        .size(width, 18.0f)
        .text(text)
        .fontSize(13.0f)
        .color("#667085")
        .build();
}

void composeNetworkInput(
    eui::Ui& ui,
    const std::string& id,
    float x,
    float y,
    float width,
    const std::string& value,
    const std::string& placeholder,
    networkdoctor::NetworkField field) {
    components::input(ui, id)
        .theme(theme().tokens)
        .position(x, y)
        .size(width, 40.0f)
        .value(value)
        .placeholder(placeholder)
        .fontSize(15.0f)
        .inset(10.0f)
        .transition(theme().motion)
        .onChange([field](const std::string& next) { state().setNetworkField(field, next); })
        .build();
}

void composeNetworkButton(
    eui::Ui& ui,
    const std::string& id,
    float x,
    float y,
    float width,
    const std::string& text,
    bool primary,
    bool disabled,
    std::function<void()> action) {
    components::button(ui, id)
        .theme(theme().tokens, primary)
        .position(x, y)
        .size(width, 40.0f)
        .radius(10.0f)
        .text(text)
        .fontSize(14.0f)
        .textColor(primary ? eui::Color("#FFFFFF") : eui::Color("#344054"))
        .disabled(disabled)
        .transition(theme().motion)
        .onClick(std::move(action))
        .build();
}

void composeAdapterSummary(eui::Ui& ui, float x, float y, float width) {
    ui.rect("network.detail.background")
        .position(x, y)
        .size(width, 100.0f)
        .color("#F8FAFC")
        .radius(12.0f)
        .border(1.0f, eui::Color("#E2E8F0"))
        .build();

    const networkdoctor::NetworkAdapter* adapter = state().selectedAdapter();
    if (adapter == nullptr) {
        ui.text("network.detail.empty")
            .position(x + 16.0f, y + 32.0f)
            .size(width - 32.0f, 28.0f)
            .text("点击“刷新网卡”读取当前网络适配器")
            .fontSize(14.0f)
            .color("#667085")
            .build();
        return;
    }

    const std::string ipv4 = adapter->ipv4.empty() ? "未配置" : adapter->ipv4;
    const std::string ipv6 = adapter->ipv6.empty() ? "未配置" : adapter->ipv6;
    const std::string gateway = adapter->gateway.empty() ? "未配置" : adapter->gateway;
    const std::string dns = adapter->dnsServers.empty() ? "未配置" : joinDnsServers(adapter->dnsServers);
    const std::string ipv4Text = adapter->prefixLength.has_value()
        ? ipv4 + "/" + std::to_string(adapter->prefixLength.value())
        : ipv4;
    const float cellWidth = (width - 32.0f) / 4.0f;

    components::text(ui, "network.detail.status", theme().tokens)
        .position(x + 16.0f, y + 12.0f)
        .size(width * 0.7f, 24.0f)
        .text(adapter->name + " · " + adapter->status + (adapter->isDhcp ? " · DHCP" : " · 静态"))
        .fontSize(16.0f)
        .fontWeight(740)
        .color(adapter->status == "Up" ? "#16A34A" : "#D97706")
        .build();
    components::text(ui, "network.detail.speed", theme().tokens)
        .position(x + width - 112.0f, y + 14.0f)
        .size(96.0f, 20.0f)
        .text(adapter->linkSpeed)
        .fontSize(14.0f)
        .color("#667085")
        .horizontalAlign(eui::HorizontalAlign::Right)
        .build();

    const std::array<std::string, 4> labels = {"IPv4", "IPv6", "网关", "DNS"};
    const std::array<std::string, 4> values = {ipv4Text, ipv6, gateway, dns};
    for (std::size_t index = 0; index < labels.size(); ++index) {
        const float cellX = x + 16.0f + static_cast<float>(index) * cellWidth;
        ui.text("network.detail.label." + std::to_string(index))
            .position(cellX, y + 45.0f)
            .size(cellWidth - 10.0f, 18.0f)
            .text(labels[index])
            .fontSize(12.0f)
            .color("#98A2AC")
            .build();
        ui.text("network.detail.value." + std::to_string(index))
            .position(cellX, y + 63.0f)
            .size(cellWidth - 10.0f, 28.0f)
            .text(values[index])
            .fontSize(14.0f)
            .color("#344054")
            .wrap(true)
            .build();
    }
}

void composeNetworkTools(eui::Ui& ui, const eui::Screen& screen) {
    const float canvasX = kNavigationWidth;
    const float canvasWidth = std::max(0.0f, screen.width - canvasX);
    const float contentX = canvasX + kPagePadding;
    const float contentWidth = std::max(0.0f, canvasWidth - kPagePadding * 2.0f);
    const float cardX = contentX + 18.0f;
    const float cardWidth = std::max(0.0f, contentWidth - 36.0f);
    const float cardY = 94.0f;

    ui.rect("network.background")
        .position(canvasX, 0.0f)
        .size(canvasWidth, screen.height)
        .color("#F4F6F8")
        .build();
    composePageTitle(ui, contentX, contentWidth, "网卡工具", "查看适配器详情，配置静态 IP 或切回 DHCP。");
    components::card(ui, "network.card")
        .position(contentX, cardY)
        .size(contentWidth, 480.0f)
        .padding(18.0f)
        .theme(theme().tokens)
        .color(eui::Color("#FFFFFF"))
        .radius(20.0f)
        .border(1.0f, eui::Color("#E2E8F0"))
        .content([] {})
        .build();

    std::vector<std::string> adapterNames;
    adapterNames.reserve(state().adapters.size());
    for (const networkdoctor::NetworkAdapter& adapter : state().adapters) {
        adapterNames.push_back(adapter.name + " · " + adapter.status);
    }
    ui.stack("network.adapter.host")
        .position(cardX, cardY + 18.0f)
        .size(cardWidth - 104.0f, 180.0f)
        .content([&] {
            components::dropdown(ui, "network.adapter")
                .theme(theme().tokens)
                .size(cardWidth - 104.0f, 40.0f)
            .items(std::move(adapterNames))
            .selected(state().selectedAdapterIndex)
                .placeholder("请选择网络适配器")
                .open(state().adapterDropdownOpen)
                .zIndex(40)
                .transition(theme().motion)
                .onOpenChange([](bool open) { state().setAdapterDropdownOpen(open); })
                .onChange([](int index) { state().setAdapterIndex(index); })
                .build();
        })
        .build();
    composeNetworkButton(
        ui,
        "network.refresh",
        cardX + cardWidth - 96.0f,
        cardY + 18.0f,
        96.0f,
        state().busy && state().operation == networkdoctor::CliCommand::Adapters ? "正在读取" : "刷新网卡",
        false,
        state().busy,
        [] { startOperation(networkdoctor::CliCommand::Adapters); });

    composeAdapterSummary(ui, cardX, cardY + 72.0f, cardWidth);

    ui.rect("network.public.background")
        .position(cardX, cardY + 178.0f)
        .size(cardWidth, 54.0f)
        .color("#F8FAFC")
        .radius(12.0f)
        .border(1.0f, eui::Color("#E2E8F0"))
        .build();
    components::text(ui, "network.public.title", theme().tokens)
        .position(cardX + 14.0f, cardY + 192.0f)
        .size(150.0f, 20.0f)
        .text("公网 IP")
        .fontSize(15.0f)
        .fontWeight(740)
        .color("#172B4D")
        .build();
    ui.text("network.public.ipv4")
        .position(cardX + 112.0f, cardY + 192.0f)
        .size(cardWidth * 0.35f, 20.0f)
        .text("IPv4  " + valueOr(state().publicIpv4, "未检测"))
        .fontSize(14.0f)
        .color("#344054")
        .build();
    ui.text("network.public.ipv6")
        .position(cardX + cardWidth * 0.52f, cardY + 192.0f)
        .size(cardWidth * 0.40f, 34.0f)
        .text("IPv6  " + valueOr(state().publicIpv6, "未检测"))
        .fontSize(13.0f)
        .wrap(true)
        .color("#344054")
        .build();
    composeNetworkButton(
        ui,
        "network.public.refresh",
        cardX + cardWidth - 80.0f,
        cardY + 186.0f,
        80.0f,
        state().busy && state().operation == networkdoctor::CliCommand::PublicIp ? "检测中" : "检测公网",
        false,
        state().busy,
        [] { startOperation(networkdoctor::CliCommand::PublicIp); });

    ui.rect("network.static.separator")
        .position(cardX, cardY + 238.0f)
        .size(cardWidth, 1.0f)
        .color("#E2E8F0")
        .build();

    components::text(ui, "network.static.title", theme().tokens)
        .position(cardX, cardY + 250.0f)
        .size(cardWidth, 24.0f)
        .text("静态 IP 配置")
        .fontSize(18.0f)
        .fontWeight(740)
        .color("#172B4D")
        .build();

    const float gap = 8.0f;
    const float third = (cardWidth - gap * 2.0f) / 3.0f;
    const float half = (cardWidth - gap) / 2.0f;
    composeFieldLabel(ui, "network.static.ip.label", cardX, cardY + 276.0f, third, "IP 地址");
    composeFieldLabel(ui, "network.static.mask.label", cardX + third + gap, cardY + 276.0f, third, "子网掩码");
    composeFieldLabel(ui, "network.static.gateway.label", cardX + (third + gap) * 2.0f, cardY + 276.0f, third, "网关");
    composeNetworkInput(ui, "network.static.ip", cardX, cardY + 298.0f, third, state().staticIp, "192.0.2.10", networkdoctor::NetworkField::StaticIp);
    composeNetworkInput(ui, "network.static.mask", cardX + third + gap, cardY + 298.0f, third, state().staticMask, "255.255.255.0", networkdoctor::NetworkField::StaticMask);
    composeNetworkInput(ui, "network.static.gateway", cardX + (third + gap) * 2.0f, cardY + 298.0f, third, state().staticGateway, "192.0.2.1", networkdoctor::NetworkField::StaticGateway);
    composeFieldLabel(ui, "network.static.dns1.label", cardX, cardY + 342.0f, half, "DNS 1");
    composeFieldLabel(ui, "network.static.dns2.label", cardX + half + gap, cardY + 342.0f, half, "DNS 2");
    composeNetworkInput(ui, "network.static.dns1", cardX, cardY + 364.0f, half, state().staticDns1, "1.1.1.1", networkdoctor::NetworkField::StaticDns1);
    composeNetworkInput(ui, "network.static.dns2", cardX + half + gap, cardY + 364.0f, half, state().staticDns2, "8.8.8.8", networkdoctor::NetworkField::StaticDns2);

    const float actionWidth = (cardWidth - gap * 3.0f) / 4.0f;
    composeNetworkButton(ui, "network.fill", cardX, cardY + 414.0f, actionWidth, "填入当前", false, state().selectedAdapter() == nullptr, [] { state().fillSelectedStaticForm(); });
    composeNetworkButton(ui, "network.static.apply", cardX + (actionWidth + gap), cardY + 414.0f, actionWidth, "应用静态 IP", true, state().busy || state().selectedAdapter() == nullptr, [] { state().setConfirmation(networkdoctor::Confirmation::StaticIp, true); });
    composeNetworkButton(ui, "network.dhcp", cardX + (actionWidth + gap) * 2.0f, cardY + 414.0f, actionWidth, "切换 DHCP", false, state().busy || state().selectedAdapter() == nullptr, [] { state().setConfirmation(networkdoctor::Confirmation::Dhcp, true); });
    composeNetworkButton(ui, "network.clear", cardX + (actionWidth + gap) * 3.0f, cardY + 414.0f, actionWidth, "清空表单", false, state().busy, [] {
        state().setNetworkField(networkdoctor::NetworkField::StaticIp, "");
        state().setNetworkField(networkdoctor::NetworkField::StaticMask, "");
        state().setNetworkField(networkdoctor::NetworkField::StaticGateway, "");
        state().setNetworkField(networkdoctor::NetworkField::StaticDns1, "");
        state().setNetworkField(networkdoctor::NetworkField::StaticDns2, "");
    });
}

void composeRouter(eui::Ui& ui, const eui::Screen& screen) {
    const float canvasX = kNavigationWidth;
    const float canvasWidth = std::max(0.0f, screen.width - canvasX);
    const float contentX = canvasX + kPagePadding;
    const float contentWidth = std::max(0.0f, canvasWidth - kPagePadding * 2.0f);
    const float cardX = contentX + 18.0f;
    const float cardWidth = std::max(0.0f, contentWidth - 36.0f);
    const float cardY = 116.0f;

    ui.rect("router.background")
        .position(canvasX, 0.0f)
        .size(canvasWidth, screen.height)
        .color("#F4F6F8")
        .build();
    composePageTitle(ui, contentX, contentWidth, "路由器", "安全保存路由器密码并执行 PPPoE 重拨。");
    components::card(ui, "router.card")
        .position(contentX, cardY)
        .size(contentWidth, 330.0f)
        .padding(18.0f)
        .theme(theme().tokens)
        .color(eui::Color("#FFFFFF"))
        .radius(20.0f)
        .border(1.0f, eui::Color("#E2E8F0"))
        .content([] {})
        .build();

    composeFieldLabel(ui, "router.host.label", cardX, cardY + 18.0f, cardWidth, "路由器地址");
    composeNetworkInput(ui, "router.host", cardX, cardY + 40.0f, cardWidth, state().routerHost, "192.168.31.1 或 router.local", networkdoctor::NetworkField::RouterHost);
    composeFieldLabel(ui, "router.password.label", cardX, cardY + 90.0f, cardWidth, "路由器密码");
    components::input(ui, "router.password")
        .theme(theme().tokens)
        .position(cardX, cardY + 112.0f)
        .size(cardWidth, 38.0f)
        .value(ui.state<std::string>("router.password"))
        .placeholder("密码仅通过匿名管道 stdin 传给 CLI")
        .fontSize(15.0f)
        .fontFamily("monospace")
        .inset(10.0f)
        .transition(theme().motion)
        .onChange([&ui](const std::string& value) { ui.state<std::string>("router.password") = value; })
        .build();

    const float gap = 10.0f;
    const float buttonWidth = (cardWidth - gap) / 2.0f;
    composeNetworkButton(ui, "router.save", cardX, cardY + 172.0f, buttonWidth, state().busy && state().operation == networkdoctor::CliCommand::RouterSave ? "正在保存" : "保存密码", false, state().busy, [&ui] {
        std::string password = ui.state<std::string>("router.password");
        if (startRouterOperation(networkdoctor::CliCommand::RouterSave, password)) {
            std::fill(password.begin(), password.end(), '\0');
            password.clear();
            ui.state<std::string>("router.password").clear();
            app::requestUpdate();
        }
    });
    composeNetworkButton(ui, "router.redial", cardX + buttonWidth + gap, cardY + 172.0f, buttonWidth, state().busy && state().operation == networkdoctor::CliCommand::Redial ? "正在重拨" : "PPPoE 重拨", true, state().busy, [] { state().setConfirmation(networkdoctor::Confirmation::Redial, true); });

    const std::string result = state().errorMessage.empty() ? state().routerResult : state().errorMessage;
    ui.rect("router.result.background")
        .position(cardX, cardY + 226.0f)
        .size(cardWidth, 66.0f)
        .color(state().errorMessage.empty() ? "#F4F8FF" : "#FEF2F2")
        .radius(12.0f)
        .border(1.0f, eui::Color(state().errorMessage.empty() ? "#D6E4FF" : "#FECACA"))
        .build();
    components::text(ui, "router.result.title", theme().tokens)
        .position(cardX + 14.0f, cardY + 238.0f)
        .size(cardWidth - 28.0f, 20.0f)
        .text(state().errorMessage.empty() ? "路由器状态" : "操作失败")
        .fontSize(13.0f)
        .fontWeight(740)
        .color(state().errorMessage.empty() ? "#172B4D" : "#DC2626")
        .build();
    ui.text("router.result.message")
        .position(cardX + 14.0f, cardY + 262.0f)
        .size(cardWidth - 28.0f, 20.0f)
        .text(result.empty() ? "输入路由器地址和密码后执行操作" : result)
        .fontSize(12.0f)
        .color(state().errorMessage.empty() ? "#667085" : "#DC2626")
        .build();
}

void composeGuide(eui::Ui& ui, const eui::Screen& screen) {
    const float canvasX = kNavigationWidth;
    const float canvasWidth = std::max(0.0f, screen.width - canvasX);
    const float contentX = canvasX + kPagePadding;
    const float contentWidth = std::max(0.0f, canvasWidth - kPagePadding * 2.0f);

    ui.rect("guide.background")
        .position(canvasX, 0.0f)
        .size(canvasWidth, screen.height)
        .color("#F4F6F8")
        .build();
    composePageTitle(ui, contentX, contentWidth, "使用说明", "按顺序完成检测、修复和联网验证。");
    components::card(ui, "guide.card")
        .position(contentX, 94.0f)
        .size(contentWidth, 500.0f)
        .padding(24.0f)
        .theme(theme().tokens)
        .color(eui::Color("#FFFFFF"))
        .radius(20.0f)
        .border(1.0f, eui::Color("#E2E8F0"))
        .content([] {})
        .build();
    components::text(ui, "guide.steps", theme().tokens)
        .position(contentX + 24.0f, 118.0f)
        .size(contentWidth - 48.0f, 440.0f)
        .text(
            "代理检测与修复\n\n"
            "点击“开始检测”查看活动代理；确认后点击“关闭代理”，程序会先备份，完成后可联网验证或撤销。\n\n"
            "网卡与公网 IP\n\n"
            "进入“网卡工具”刷新适配器，查看 IPv4、IPv6、网关和 DNS；可检测公网 IP、填入当前配置、应用静态 IP 或切换 DHCP。\n\n"
            "路由器\n\n"
            "进入“路由器”填写地址和管理密码；保存后密码由 Windows DPAPI 加密，重拨会短暂断开网络。\n\n"
            "安全提示\n\n"
            "所有系统修改都需要确认；只执行固定命令，不上传网络数据。")
        .fontSize(16.0f)
        .lineHeight(26.0f)
        .wrap(true)
        .color("#344054")
        .build();
}

void composeAbout(eui::Ui& ui, const eui::Screen& screen) {
    const float canvasX = kNavigationWidth;
    const float canvasWidth = std::max(0.0f, screen.width - canvasX);
    const float contentX = canvasX + kPagePadding;
    const float contentWidth = std::max(0.0f, canvasWidth - kPagePadding * 2.0f);

    ui.rect("about.background")
        .position(canvasX, 0.0f)
        .size(canvasWidth, screen.height)
        .color("#F4F6F8")
        .build();
    composePageTitle(ui, contentX, contentWidth, "关于工具", "安全、聚焦代理配置的 Windows 网络诊断工具。");
    components::card(ui, "about.card")
        .position(contentX, 94.0f)
        .size(contentWidth, 420.0f)
        .padding(24.0f)
        .theme(theme().tokens)
        .color(eui::Color("#FFFFFF"))
        .radius(20.0f)
        .border(1.0f, eui::Color("#E2E8F0"))
        .content([] {})
        .build();
    components::text(ui, "about.content", theme().tokens)
        .position(contentX + 24.0f, 118.0f)
        .size(contentWidth - 48.0f, 360.0f)
        .text(
            "Network Doctor Neo 2.0\n\n"
            "Windows 10/11 网络检测、代理修复与网络管理工具。\n\n"
            "功能包括：代理检测与安全修复、联网验证、网卡信息、静态 IP/DHCP、公网 IPv4/IPv6、路由器 PPPoE 重拨。\n\n"
            "安全边界：\n"
            "• 所有操作在本机完成，不上传网络数据。\n"
            "• 只执行固定白名单命令。\n"
            "• 路由器密码使用 Windows DPAPI 加密保存。\n"
            "• 修改网卡或重拨前必须确认。\n\n"
            "注意：错误网关或 PPPoE 重拨可能造成短暂断网。")
        .fontSize(15.0f)
        .lineHeight(24.0f)
        .wrap(true)
        .color("#344054")
        .build();
}

void composeOverlays(eui::Ui& ui, const eui::Screen& screen) {
    const networkdoctor::UiTheme uiTheme = theme();
    components::dialog(ui, "repair.confirmation")
        .theme(uiTheme.tokens)
        .screen(screen.width, screen.height)
        .size(440.0f, 230.0f)
        .open(state().repairConfirmationOpen)
        .title("关闭代理")
        .message("此操作会先备份当前代理设置，再关闭已发现的活动代理。是否继续？")
        .primaryText("确认关闭")
        .secondaryText("取消")
        .transition(uiTheme.motion)
        .onOpenChange([](bool open) { state().setRepairConfirmation(open); })
        .onPrimary([] {
            state().setRepairConfirmation(false);
            startOperation(networkdoctor::CliCommand::Repair);
        })
        .build();

    components::dialog(ui, "network.static.confirmation")
        .theme(uiTheme.tokens)
        .screen(screen.width, screen.height)
        .size(440.0f, 230.0f)
        .open(state().confirmation == networkdoctor::Confirmation::StaticIp)
        .title("应用静态 IP")
        .message("此操作会替换当前网卡的 IPv4 与 DNS 配置。确认已核对地址并继续？")
        .primaryText("确认应用")
        .secondaryText("取消")
        .transition(uiTheme.motion)
        .onOpenChange([](bool open) {
            if (!open) {
                state().setConfirmation(networkdoctor::Confirmation::None, false);
            }
        })
        .onPrimary([] {
            state().setConfirmation(networkdoctor::Confirmation::None, false);
            networkdoctor::CliRequest request;
            request.command = networkdoctor::CliCommand::IpStatic;
            request.adapter = state().selectedAdapterName();
            request.ip = state().staticIp;
            request.mask = state().staticMask;
            request.gateway = state().staticGateway;
            request.dns1 = state().staticDns1;
            request.dns2 = state().staticDns2;
            startRequest(std::move(request));
        })
        .build();

    components::dialog(ui, "network.dhcp.confirmation")
        .theme(uiTheme.tokens)
        .screen(screen.width, screen.height)
        .size(440.0f, 220.0f)
        .open(state().confirmation == networkdoctor::Confirmation::Dhcp)
        .title("切换 DHCP")
        .message("此操作会释放当前静态 IPv4 与 DNS 配置，并重新从 DHCP 获取地址。是否继续？")
        .primaryText("确认切换")
        .secondaryText("取消")
        .transition(uiTheme.motion)
        .onOpenChange([](bool open) {
            if (!open) {
                state().setConfirmation(networkdoctor::Confirmation::None, false);
            }
        })
        .onPrimary([] {
            state().setConfirmation(networkdoctor::Confirmation::None, false);
            networkdoctor::CliRequest request;
            request.command = networkdoctor::CliCommand::IpDhcp;
            request.adapter = state().selectedAdapterName();
            startRequest(std::move(request));
        })
        .build();

    components::dialog(ui, "router.redial.confirmation")
        .theme(uiTheme.tokens)
        .screen(screen.width, screen.height)
        .size(440.0f, 230.0f)
        .open(state().confirmation == networkdoctor::Confirmation::Redial)
        .title("PPPoE 重拨")
        .message("此操作会中断并重新建立路由器的 PPPoE 连接，过程中网络可能短暂断开。是否继续？")
        .primaryText("确认重拨")
        .secondaryText("取消")
        .transition(uiTheme.motion)
        .onOpenChange([](bool open) {
            if (!open) {
                state().setConfirmation(networkdoctor::Confirmation::None, false);
            }
        })
        .onPrimary([&ui] {
            state().setConfirmation(networkdoctor::Confirmation::None, false);
            std::string password = ui.state<std::string>("router.password");
            if (startRouterOperation(networkdoctor::CliCommand::Redial, password)) {
                std::fill(password.begin(), password.end(), '\0');
                password.clear();
                ui.state<std::string>("router.password").clear();
                app::requestUpdate();
            }
        })
        .build();

    components::toast(ui, "application.toast")
        .theme(uiTheme.tokens)
        .screen(screen.width, screen.height)
        .size(390.0f, 92.0f)
        .visible(state().toastVisible)
        .title(state().toastTitle)
        .message(state().toastMessage)
        .duration(3.5f)
        .transition(uiTheme.motion)
        .onDismiss([] { state().dismissToast(); })
        .onAutoDismiss([] { state().dismissToast(); })
        .build();
}

}

const DslAppConfig& dslAppConfig() {
    static const DslAppConfig config = DslAppConfig{}
        .title("Network Doctor")
        .pageId("network_doctor_neo")
        .clearColor(eui::Color("#F4F6F8"))
        .windowSize(1080, 680)
        .minWindowSize(920, 600)
        .centerWindow()
        .showDebugStatsInTitle(false)
        .showDebugOverlay(false)
        .fps(60.0);
    return config;
}

void compose(eui::Ui& ui, const eui::Screen& screen) {
    startInitialInspect();

    ui.stack("root")
        .size(screen.width, screen.height)
        .content([&] {
            composeNavigation(ui, screen.height);
            if (state().page == networkdoctor::Page::Diagnose) {
                composeDiagnosis(ui, screen);
            } else if (state().page == networkdoctor::Page::NetworkTools) {
                composeNetworkTools(ui, screen);
            } else if (state().page == networkdoctor::Page::Router) {
                composeRouter(ui, screen);
            } else if (state().page == networkdoctor::Page::Guide) {
                composeGuide(ui, screen);
            } else {
                composeAbout(ui, screen);
            }
        })
        .build();

    composeOverlays(ui, screen);
}

}
