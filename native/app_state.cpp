#include "app_state.h"

#include "eui_neo.h"

#include <nlohmann/json.hpp>

#include <cstdint>
#include <initializer_list>
#include <stdexcept>
#include <string_view>
#include <utility>

namespace networkdoctor {
namespace {

using Json = nlohmann::json;

template <typename Value>
Value requiredValue(
    const Json& payload,
    std::initializer_list<std::string_view> names,
    std::string_view field) {
    for (std::string_view name : names) {
        const auto iterator = payload.find(name);
        if (iterator != payload.end()) {
            return iterator->get<Value>();
        }
    }
    throw std::runtime_error("CLI JSON 缺少字段 " + std::string(field));
}

std::string stringValue(const Json& value, std::string_view field) {
    if (value.is_string()) {
        return value.get<std::string>();
    }
    throw std::runtime_error("CLI JSON 字段 " + std::string(field) + " 类型无效");
}

std::string requiredString(
    const Json& payload,
    std::initializer_list<std::string_view> names,
    std::string_view field) {
    return stringValue(requiredValue<Json>(payload, names, field), field);
}

std::string optionalString(
    const Json& payload,
    std::initializer_list<std::string_view> names,
    std::string_view field) {
    for (std::string_view name : names) {
        const auto iterator = payload.find(name);
        if (iterator == payload.end() || iterator->is_null()) {
            continue;
        }
        return stringValue(*iterator, field);
    }
    return {};
}

std::optional<int> optionalInt(
    const Json& payload,
    std::initializer_list<std::string_view> names,
    std::string_view field) {
    for (std::string_view name : names) {
        const auto iterator = payload.find(name);
        if (iterator == payload.end() || iterator->is_null()) {
            continue;
        }
        if (iterator->is_number_integer()) {
            return iterator->get<int>();
        }
        throw std::runtime_error("CLI JSON 字段 " + std::string(field) + " 类型无效");
    }
    return std::nullopt;
}

bool optionalBool(
    const Json& payload,
    std::initializer_list<std::string_view> names,
    std::string_view field) {
    for (std::string_view name : names) {
        const auto iterator = payload.find(name);
        if (iterator == payload.end() || iterator->is_null()) {
            continue;
        }
        if (iterator->is_boolean()) {
            return iterator->get<bool>();
        }
        throw std::runtime_error("CLI JSON 字段 " + std::string(field) + " 类型无效");
    }
    return false;
}

std::string enumValue(const Json& value, std::string_view field, const std::vector<std::string>& names) {
    if (value.is_number_unsigned()) {
        const auto index = value.get<std::size_t>();
        if (index < names.size()) {
            return names[index];
        }
    } else if (value.is_number_integer()) {
        const auto index = value.get<int>();
        if (index >= 0 && static_cast<std::size_t>(index) < names.size()) {
            return names[static_cast<std::size_t>(index)];
        }
    } else if (value.is_string()) {
        return value.get<std::string>();
    }
    throw std::runtime_error("CLI JSON 字段 " + std::string(field) + " 枚举值无效");
}

std::vector<ProxyFinding> parseFindings(const Json& payload) {
    const auto& values = requiredValue<Json>(payload, {"findings", "Findings"}, "findings");
    if (!values.is_array()) {
        throw std::runtime_error("CLI JSON 字段 findings 必须是数组");
    }

    const std::vector<std::string> sources = {"WinInet", "WinHttp", "Environment", "Browser", "Unknown"};
    std::vector<ProxyFinding> findings;
    findings.reserve(values.size());
    for (const auto& value : values) {
        findings.push_back({
            enumValue(requiredValue<Json>(value, {"Source", "source"}, "Source"), "Source", sources),
            requiredString(value, {"Scope", "scope"}, "Scope"),
            requiredString(value, {"Name", "name"}, "Name"),
            requiredString(value, {"Address", "address"}, "Address"),
            requiredString(value, {"Impact", "impact"}, "Impact"),
        });
    }
    return findings;
}

std::vector<OperationItem> parseItems(const Json& payload) {
    const auto& values = requiredValue<Json>(payload, {"items", "Items"}, "items");
    if (!values.is_array()) {
        throw std::runtime_error("CLI JSON 字段 items 必须是数组");
    }

    const std::vector<std::string> sources = {"WinInet", "WinHttp", "Environment", "Browser", "Unknown"};
    const std::vector<std::string> statuses = {"Succeeded", "Failed", "Skipped"};
    std::vector<OperationItem> items;
    items.reserve(values.size());
    for (const auto& value : values) {
        items.push_back({
            enumValue(requiredValue<Json>(value, {"Source", "source"}, "Source"), "Source", sources),
            requiredString(value, {"Name", "name"}, "Name"),
            enumValue(requiredValue<Json>(value, {"Status", "status"}, "Status"), "Status", statuses),
            requiredString(value, {"Message", "message"}, "Message"),
        });
    }
    return items;
}

std::vector<NetworkAdapter> parseAdapters(const Json& payload) {
    const auto& values = requiredValue<Json>(payload, {"adapters", "Adapters"}, "adapters");
    if (!values.is_array()) {
        throw std::runtime_error("CLI JSON 字段 adapters 必须是数组");
    }

    std::vector<NetworkAdapter> adapters;
    adapters.reserve(values.size());
    for (const auto& value : values) {
        NetworkAdapter adapter;
        adapter.name = requiredString(value, {"Name", "name"}, "Name");
        adapter.description = requiredString(value, {"Description", "description"}, "Description");
        adapter.status = requiredString(value, {"Status", "status"}, "Status");
        adapter.linkSpeed = requiredString(value, {"LinkSpeed", "linkSpeed"}, "LinkSpeed");
        adapter.ifIndex = optionalInt(value, {"IfIndex", "ifIndex"}, "IfIndex").value_or(0);
        adapter.ipv4 = optionalString(value, {"Ipv4", "ipv4"}, "Ipv4");
        adapter.prefixLength = optionalInt(value, {"PrefixLength", "prefixLength"}, "PrefixLength");
        adapter.ipv6 = optionalString(value, {"Ipv6", "ipv6"}, "Ipv6");
        adapter.isDhcp = optionalBool(value, {"IsDhcp", "isDhcp"}, "IsDhcp");
        adapter.gateway = optionalString(value, {"Gateway", "gateway"}, "Gateway");
        const auto dns = value.find("DnsServers");
        const auto dnsLower = value.find("dnsServers");
        const Json* dnsValue = dns != value.end() ? &*dns : (dnsLower != value.end() ? &*dnsLower : nullptr);
        if (dnsValue != nullptr && !dnsValue->is_null()) {
            if (!dnsValue->is_array()) {
                throw std::runtime_error("CLI JSON 字段 DnsServers 必须是数组");
            }
            for (const auto& server : *dnsValue) {
                adapter.dnsServers.push_back(stringValue(server, "DnsServers"));
            }
        }
        adapters.push_back(std::move(adapter));
    }
    return adapters;
}

std::string payloadErrorMessage(const Json& payload, const CliResult& result) {
    const auto error = payload.find("error");
    if (error != payload.end() && error->is_string() && !error->get<std::string>().empty()) {
        return error->get<std::string>();
    }
    if (!result.error.empty()) {
        return result.error;
    }
    const auto summary = payload.find("summary");
    if (summary != payload.end() && summary->is_string() && !summary->get<std::string>().empty()) {
        return summary->get<std::string>();
    }
    return "CLI 返回了失败状态";
}

void showToast(AppState& state, std::string title, std::string message) {
    state.toastTitle = std::move(title);
    state.toastMessage = std::move(message);
    state.toastVisible = true;
}

std::string maskFromPrefix(int prefixLength) {
    if (prefixLength < 0 || prefixLength > 32) {
        return {};
    }
    const std::uint32_t mask = prefixLength == 0 ? 0u : 0xFFFFFFFFu << (32 - prefixLength);
    return std::to_string((mask >> 24) & 0xFFu) + "." +
           std::to_string((mask >> 16) & 0xFFu) + "." +
           std::to_string((mask >> 8) & 0xFFu) + "." +
           std::to_string(mask & 0xFFu);
}

void fillStaticFields(AppState& state, const NetworkAdapter& adapter) {
    state.staticIp = adapter.ipv4;
    state.staticMask = adapter.prefixLength.has_value() ? maskFromPrefix(adapter.prefixLength.value()) : "";
    state.staticGateway = adapter.gateway;
    state.staticDns1 = adapter.dnsServers.size() > 0 ? adapter.dnsServers[0] : "";
    state.staticDns2 = adapter.dnsServers.size() > 1 ? adapter.dnsServers[1] : "";
}

}

AppState& AppState::instance() {
    static AppState state;
    return state;
}

bool AppState::beginOperation(CliCommand value) {
    if (busy) {
        return false;
    }
    operation = value;
    busy = true;
    errorMessage.clear();
    verificationPassed.reset();
    if (value == CliCommand::RouterSave || value == CliCommand::Redial) {
        routerResult.clear();
    }
    requestUiUpdate();
    return true;
}

void AppState::applyResult(CliCommand command, const CliResult& result) {
    try {
        const Json payload = Json::parse(result.output);
        if (!payload.is_object()) {
            throw std::runtime_error("CLI JSON 根节点必须是对象");
        }

        const bool payloadOk = requiredValue<bool>(payload, {"ok", "Ok"}, "ok");
        if (!result.ok || !payloadOk) {
            if (command == CliCommand::Verify) {
                verificationPassed = false;
            }
            failOperation(payloadErrorMessage(payload, result));
            return;
        }

        if (command == CliCommand::Inspect) {
            findings = parseFindings(payload);
            score = requiredValue<int>(payload, {"score", "Score"}, "score");
            summary = requiredString(payload, {"summary", "Summary"}, "summary");
        } else if (command == CliCommand::Repair) {
            operationItems = parseItems(payload);
            backupPath = requiredString(payload, {"backup", "Backup"}, "backup");
            summary = requiredString(payload, {"summary", "Summary"}, "summary");
        } else if (command == CliCommand::Restore) {
            operationItems = parseItems(payload);
            backupPath.clear();
            summary = requiredString(payload, {"summary", "Summary"}, "summary");
        } else if (command == CliCommand::Verify) {
            verificationPassed = true;
            summary = requiredString(payload, {"summary", "Summary"}, "summary");
        } else if (command == CliCommand::Adapters) {
            adapters = parseAdapters(payload);
            selectedAdapterIndex = adapters.empty() ? -1 : 0;
            if (!adapters.empty()) {
                fillStaticFields(*this, adapters.front());
            }
            summary = requiredString(payload, {"summary", "Summary"}, "summary");
        } else if (command == CliCommand::PublicIp) {
            publicIpv4 = optionalString(payload, {"ipv4", "Ipv4"}, "ipv4");
            publicIpv6 = optionalString(payload, {"ipv6", "Ipv6"}, "ipv6");
            summary = requiredString(payload, {"summary", "Summary"}, "summary");
        } else if (command == CliCommand::IpStatic || command == CliCommand::IpDhcp) {
            summary = requiredString(payload, {"summary", "Summary"}, "summary");
        } else if (command == CliCommand::RouterSave || command == CliCommand::Redial) {
            routerResult = requiredString(payload, {"summary", "Summary"}, "summary");
            summary = routerResult;
        }

        busy = false;
        errorMessage.clear();
        showToast(*this, command == CliCommand::RouterSave || command == CliCommand::Redial ? "路由器操作完成" : "操作完成", summary);
        requestUiUpdate();
    } catch (const std::exception& exception) {
        failOperation(exception.what());
    }
}

void AppState::failOperation(std::string message) {
    busy = false;
    errorMessage = std::move(message);
    if (operation == CliCommand::RouterSave || operation == CliCommand::Redial) {
        routerResult = errorMessage;
    }
    showToast(*this, "操作失败", errorMessage);
    requestUiUpdate();
}

void AppState::setPage(Page value) {
    if (page == value) {
        return;
    }
    page = value;
    confirmation = Confirmation::None;
    repairConfirmationOpen = false;
    requestUiUpdate();
}

void AppState::setAdapterIndex(int index) {
    if (index < -1 || index >= static_cast<int>(adapters.size())) {
        return;
    }
    selectedAdapterIndex = index;
    if (index >= 0) {
        fillStaticFields(*this, adapters[static_cast<std::size_t>(index)]);
    }
    requestUiUpdate();
}

void AppState::setAdapterDropdownOpen(bool open) {
    adapterDropdownOpen = open;
    requestUiUpdate();
}

void AppState::fillSelectedStaticForm() {
    const NetworkAdapter* adapter = selectedAdapter();
    if (adapter != nullptr) {
        fillStaticFields(*this, *adapter);
        requestUiUpdate();
    }
}

void AppState::setNetworkField(NetworkField field, std::string value) {
    switch (field) {
        case NetworkField::StaticIp:
            staticIp = std::move(value);
            break;
        case NetworkField::StaticMask:
            staticMask = std::move(value);
            break;
        case NetworkField::StaticGateway:
            staticGateway = std::move(value);
            break;
        case NetworkField::StaticDns1:
            staticDns1 = std::move(value);
            break;
        case NetworkField::StaticDns2:
            staticDns2 = std::move(value);
            break;
        case NetworkField::RouterHost:
            routerHost = std::move(value);
            break;
    }
    requestUiUpdate();
}

void AppState::setConfirmation(Confirmation value, bool open) {
    confirmation = open ? value : Confirmation::None;
    requestUiUpdate();
}

void AppState::setRepairConfirmation(bool open) {
    repairConfirmationOpen = open;
    requestUiUpdate();
}

void AppState::dismissToast() {
    toastVisible = false;
    requestUiUpdate();
}

const NetworkAdapter* AppState::selectedAdapter() const {
    if (selectedAdapterIndex < 0 || selectedAdapterIndex >= static_cast<int>(adapters.size())) {
        return nullptr;
    }
    return &adapters[static_cast<std::size_t>(selectedAdapterIndex)];
}

std::string AppState::selectedAdapterName() const {
    const NetworkAdapter* adapter = selectedAdapter();
    return adapter == nullptr ? std::string{} : adapter->name;
}

void AppState::requestUiUpdate() const {
    app::requestUpdate();
}

}
