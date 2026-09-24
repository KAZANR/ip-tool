#pragma once

#include "cli_client.h"

#include <optional>
#include <string>
#include <vector>

namespace networkdoctor {

enum class Page {
    Diagnose,
    NetworkTools,
    Router,
    Guide,
    About
};

enum class NetworkField {
    StaticIp,
    StaticMask,
    StaticGateway,
    StaticDns1,
    StaticDns2,
    RouterHost
};

enum class Confirmation {
    None,
    StaticIp,
    Dhcp,
    Redial
};

struct ProxyFinding {
    std::string source;
    std::string scope;
    std::string name;
    std::string address;
    std::string impact;
};

struct OperationItem {
    std::string source;
    std::string name;
    std::string status;
    std::string message;
};

struct NetworkAdapter {
    std::string name;
    std::string description;
    std::string status;
    std::string linkSpeed;
    int ifIndex = 0;
    std::string ipv4;
    std::optional<int> prefixLength;
    std::string ipv6;
    bool isDhcp = false;
    std::string gateway;
    std::vector<std::string> dnsServers;
};

class AppState {
public:
    static AppState& instance();

    bool beginOperation(CliCommand command);
    void applyResult(CliCommand command, const CliResult& result);
    void failOperation(std::string message);
    void setPage(Page page);
    void setAdapterIndex(int index);
    void setAdapterDropdownOpen(bool open);
    void fillSelectedStaticForm();
    void setNetworkField(NetworkField field, std::string value);
    void setConfirmation(Confirmation confirmation, bool open);
    void setRepairConfirmation(bool open);
    void dismissToast();

    const NetworkAdapter* selectedAdapter() const;
    std::string selectedAdapterName() const;

    Page page = Page::Diagnose;
    CliCommand operation = CliCommand::Inspect;
    bool busy = false;
    int score = 0;
    std::string summary = "等待首次检测";
    std::vector<ProxyFinding> findings;
    std::vector<OperationItem> operationItems;
    std::string backupPath;
    std::optional<bool> verificationPassed;
    std::string errorMessage;
    bool repairConfirmationOpen = false;
    std::vector<NetworkAdapter> adapters;
    int selectedAdapterIndex = -1;
    bool adapterDropdownOpen = false;
    std::string publicIpv4;
    std::string publicIpv6;
    std::string staticIp;
    std::string staticMask;
    std::string staticGateway;
    std::string staticDns1;
    std::string staticDns2;
    std::string routerHost;
    std::string routerResult;
    Confirmation confirmation = Confirmation::None;
    bool toastVisible = false;
    std::string toastTitle;
    std::string toastMessage;

private:
    void requestUiUpdate() const;
};

}
