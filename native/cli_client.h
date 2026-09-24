#pragma once

#include <string>
#include <string_view>
#include <vector>

namespace networkdoctor {

enum class CliCommand {
    Inspect,
    Repair,
    Restore,
    Verify,
    Adapters,
    PublicIp,
    IpStatic,
    IpDhcp,
    RouterSave,
    Redial
};

struct CliRequest {
    CliCommand command = CliCommand::Inspect;
    std::string backupPath;
    std::string adapter;
    std::string ip;
    std::string mask;
    std::string gateway;
    std::string dns1;
    std::string dns2;
    std::string routerHost;
    std::string stdinText;
};

struct CliResult {
    bool ok = false;
    int exitCode = -1;
    std::string output;
    std::string error;
};

class CliClient {
public:
    explicit CliClient(std::wstring executablePath);

    static std::wstring resolveExecutablePath();
    static bool isExpectedBackupPath(std::string_view backupPath);
    static bool isRouterHost(std::string_view host);
    static std::vector<std::wstring> buildCommandLineArguments(const CliRequest& request);

    bool isAvailable() const;
    CliResult run(CliCommand command) const;
    CliResult run(const CliRequest& request) const;
    CliResult restore(std::string_view backupPath) const;

private:
    CliResult runCommand(const CliRequest& request) const;

    std::wstring executablePath_;
};

}
