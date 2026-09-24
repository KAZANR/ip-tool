#include "app_state.h"
#include "cli_client.h"

#include <stdexcept>
#include <string>
#include <vector>

namespace app {
void requestUpdate() {
}
}

namespace {

void expect(bool condition) {
    if (!condition) {
        throw std::runtime_error("C++ 单元测试断言失败");
    }
}

using networkdoctor::AppState;
using networkdoctor::CliClient;
using networkdoctor::CliCommand;
using networkdoctor::CliRequest;

void testFixedCommandArguments() {
    CliRequest request;
    request.command = CliCommand::Adapters;
    expect(CliClient::buildCommandLineArguments(request) == std::vector<std::wstring>{L"adapters"});

    request.command = CliCommand::PublicIp;
    expect(CliClient::buildCommandLineArguments(request) == std::vector<std::wstring>{L"public-ip"});

    request.command = CliCommand::IpStatic;
    request.adapter = "Ethernet 2";
    request.ip = "192.0.2.10";
    request.mask = "255.255.255.0";
    request.gateway = "192.0.2.1";
    request.dns1 = "1.1.1.1";
    request.dns2 = "8.8.8.8";
    const std::vector<std::wstring> expectedStatic{
        L"ip-static",
        L"Ethernet 2",
        L"192.0.2.10",
        L"255.255.255.0",
        L"192.0.2.1",
        L"1.1.1.1",
        L"8.8.8.8"};
    expect(CliClient::buildCommandLineArguments(request) == expectedStatic);

    request.command = CliCommand::IpDhcp;
    request.adapter.clear();
    request.ip.clear();
    request.mask.clear();
    request.gateway.clear();
    request.dns1.clear();
    request.dns2.clear();
    request.routerHost.clear();
    request.adapter = "Wi-Fi";
    const std::vector<std::wstring> expectedDhcp{L"ip-dhcp", L"Wi-Fi"};
    expect(CliClient::buildCommandLineArguments(request) == expectedDhcp);
}

void testRouterHostValidation() {
    expect(CliClient::isRouterHost("192.168.31.1"));
    expect(CliClient::isRouterHost("router.local"));
    expect(CliClient::isRouterHost("router-1"));
    expect(!CliClient::isRouterHost("http://192.168.31.1"));
    expect(!CliClient::isRouterHost("192.168.31.1/path"));
    expect(!CliClient::isRouterHost("192.168.31.1:80"));
    expect(!CliClient::isRouterHost("router..local"));
    expect(!CliClient::isRouterHost("-router.local"));
    expect(!CliClient::isRouterHost("router.local-"));
    expect(!CliClient::isRouterHost("router_local"));
    expect(!CliClient::isRouterHost("999.999.999.999"));
    CliRequest request;
    request.command = CliCommand::Redial;
    request.routerHost = "192.168.31.1";
    request.stdinText.clear();
    expect(CliClient::buildCommandLineArguments(request) == std::vector<std::wstring>{L"redial", L"192.168.31.1"});

    request.command = CliCommand::RouterSave;
    request.routerHost = "192.168.31.1";
    request.stdinText.clear();
    bool saveRejected = false;
    try {
        CliClient::buildCommandLineArguments(request);
    } catch (const std::invalid_argument&) {
        saveRejected = true;
    }
    expect(saveRejected);

    expect(!CliClient::isRouterHost("路由.local"));
    expect(!CliClient::isRouterHost("router local"));
    expect(!CliClient::isRouterHost("router\\local"));

    request.routerHost = "router.local/path";
    request.stdinText = "secret";
    bool rejected = false;
    try {
        CliClient::buildCommandLineArguments(request);
    } catch (const std::invalid_argument&) {
        rejected = true;
    }
    expect(rejected);
}

void testNetworkJsonParsing() {
    AppState state;
    state.applyResult(
        CliCommand::Adapters,
        networkdoctor::CliResult{
            true,
            0,
            R"({"ok":true,"adapters":[{"Name":"Ethernet","Description":"Intel I219","Status":"Up","LinkSpeed":"1 Gbps","IfIndex":12,"Ipv4":"192.0.2.10","PrefixLength":24,"Ipv6":"2001:db8::10","IsDhcp":true,"Gateway":"192.0.2.1","DnsServers":["1.1.1.1","8.8.8.8"]}],"summary":"找到 1 个网络适配器"})",
            {}});
    expect(state.adapters.size() == 1);
    expect(state.adapters[0].ipv4 == "192.0.2.10");
    expect(state.adapters[0].prefixLength == 24);
    expect(state.adapters[0].dnsServers.size() == 2);
    expect(state.selectedAdapterIndex == 0);
    expect(state.staticIp == "192.0.2.10");
    expect(state.staticMask == "255.255.255.0");
    expect(state.staticGateway == "192.0.2.1");
    expect(state.staticDns1 == "1.1.1.1");

    state.applyResult(
        CliCommand::PublicIp,
        networkdoctor::CliResult{
            true,
            0,
            R"({"ok":true,"ipv4":"203.0.113.7","ipv6":"2001:db8::7","summary":"公网 IP 检测成功"})",
            {}});
    expect(state.publicIpv4 == "203.0.113.7");
    expect(state.publicIpv6 == "2001:db8::7");

    state.applyResult(
        CliCommand::RouterSave,
        networkdoctor::CliResult{true, 0, R"({"ok":true,"summary":"路由器密码已保存"})", {}});
    expect(state.routerResult == "路由器密码已保存");
}

}

int main() {
    testFixedCommandArguments();
    testRouterHostValidation();
    testNetworkJsonParsing();
    return 0;
}
