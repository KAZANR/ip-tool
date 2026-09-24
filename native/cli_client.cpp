#include "cli_client.h"

#ifdef _WIN32

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif

#include <windows.h>

#include <algorithm>
#include <array>
#include <cctype>
#include <cwctype>
#include <filesystem>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

namespace networkdoctor {
namespace {

class UniqueHandle {
public:
    UniqueHandle() = default;
    explicit UniqueHandle(HANDLE value) : value_(value) {}
    ~UniqueHandle() { reset(); }

    UniqueHandle(const UniqueHandle&) = delete;
    UniqueHandle& operator=(const UniqueHandle&) = delete;

    UniqueHandle(UniqueHandle&& other) noexcept : value_(std::exchange(other.value_, nullptr)) {}
    UniqueHandle& operator=(UniqueHandle&& other) noexcept {
        if (this != &other) {
            reset();
            value_ = std::exchange(other.value_, nullptr);
        }
        return *this;
    }

    HANDLE get() const { return value_; }
    bool valid() const { return value_ != nullptr && value_ != INVALID_HANDLE_VALUE; }

    void reset(HANDLE value = nullptr) {
        if (valid()) {
            CloseHandle(value_);
        }
        value_ = value;
    }

private:
    HANDLE value_ = nullptr;
};

std::wstring environmentValue(const wchar_t* name) {
    DWORD length = GetEnvironmentVariableW(name, nullptr, 0);
    if (length == 0) {
        return {};
    }

    std::wstring value(length, L'\0');
    DWORD written = GetEnvironmentVariableW(name, value.data(), length);
    if (written == 0 || written >= length) {
        return {};
    }

    value.resize(written);
    return value;
}

std::string utf8FromWide(std::wstring_view value) {
    if (value.empty()) {
        return {};
    }

    int size = WideCharToMultiByte(
        CP_UTF8,
        WC_ERR_INVALID_CHARS,
        value.data(),
        static_cast<int>(value.size()),
        nullptr,
        0,
        nullptr,
        nullptr);
    if (size <= 0) {
        throw std::runtime_error("无法转换 CLI 错误信息");
    }

    std::string result(static_cast<std::size_t>(size), '\0');
    if (WideCharToMultiByte(
            CP_UTF8,
            WC_ERR_INVALID_CHARS,
            value.data(),
            static_cast<int>(value.size()),
            result.data(),
            size,
            nullptr,
            nullptr) <= 0) {
        throw std::runtime_error("无法转换 CLI 错误信息");
    }
    return result;
}

std::wstring wideFromUtf8(std::string_view value) {
    if (value.empty()) {
        return {};
    }
    if (value.size() > static_cast<std::size_t>(INT_MAX)) {
        throw std::invalid_argument("CLI 参数长度无效");
    }

    int size = MultiByteToWideChar(
        CP_UTF8,
        MB_ERR_INVALID_CHARS,
        value.data(),
        static_cast<int>(value.size()),
        nullptr,
        0);
    if (size <= 0) {
        throw std::invalid_argument("CLI 参数编码无效");
    }

    std::wstring result(static_cast<std::size_t>(size), L'\0');
    if (MultiByteToWideChar(
            CP_UTF8,
            MB_ERR_INVALID_CHARS,
            value.data(),
            static_cast<int>(value.size()),
            result.data(),
            size) <= 0) {
        throw std::invalid_argument("CLI 参数编码无效");
    }
    return result;
}

std::string windowsError(std::string_view prefix, DWORD code) {
    wchar_t* buffer = nullptr;
    DWORD size = FormatMessageW(
        FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
        nullptr,
        code,
        MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
        reinterpret_cast<wchar_t*>(&buffer),
        0,
        nullptr);

    std::wstring message;
    if (size > 0 && buffer != nullptr) {
        message.assign(buffer, size);
        LocalFree(buffer);
        while (!message.empty() && (message.back() == L'\r' || message.back() == L'\n')) {
            message.pop_back();
        }
    }

    std::string result(prefix);
    result += " (";
    result += std::to_string(static_cast<unsigned long>(code));
    result += ")";
    if (!message.empty()) {
        result += ": ";
        try {
            result += utf8FromWide(message);
        } catch (const std::exception&) {
        }
    }
    return result;
}

std::wstring quoteArgument(const std::wstring& value) {
    std::wstring quoted(1, L'"');
    std::size_t backslashes = 0;
    for (wchar_t character : value) {
        if (character == L'\\') {
            ++backslashes;
            continue;
        }
        if (character == L'"') {
            quoted.append(backslashes * 2 + 1, L'\\');
        } else {
            quoted.append(backslashes, L'\\');
        }
        backslashes = 0;
        quoted.push_back(character);
    }
    quoted.append(backslashes * 2, L'\\');
    quoted.push_back(L'"');
    return quoted;
}

std::wstring normalizedLower(std::wstring value) {
    std::transform(value.begin(), value.end(), value.begin(), [](wchar_t character) {
        return static_cast<wchar_t>(towlower(character));
    });
    return value;
}

bool hasSafeValue(std::string_view value, std::size_t maximumLength) {
    if (value.empty() || value.size() > maximumLength) {
        return false;
    }
    return std::none_of(value.begin(), value.end(), [](unsigned char character) {
        return character == 0 || character == '\r' || character == '\n' || std::iscntrl(character) != 0;
    });
}

bool isAsciiDigit(unsigned char character) {
    return character >= '0' && character <= '9';
}

bool isAsciiAlphaNumeric(unsigned char character) {
    return isAsciiDigit(character) ||
           (character >= 'A' && character <= 'Z') ||
           (character >= 'a' && character <= 'z');
}

bool isIpv4(std::string_view host) {
    std::size_t start = 0;
    int parts = 0;
    while (start <= host.size()) {
        const std::size_t end = host.find('.', start);
        const std::string_view part = host.substr(start, end == std::string_view::npos ? end : end - start);
        if (part.empty() || part.size() > 3) {
            return false;
        }
        int value = 0;
        for (unsigned char character : part) {
            if (!isAsciiDigit(character)) {
                return false;
            }
            value = value * 10 + character - '0';
        }
        if (value > 255) {
            return false;
        }
        ++parts;
        if (end == std::string_view::npos) {
            break;
        }
        start = end + 1;
    }
    return parts == 4;
}

bool isDnsHost(std::string_view host) {
    if (host.empty() || host.size() > 253 || host.front() == '.' || host.back() == '.') {
        return false;
    }
    std::size_t start = 0;
    while (start < host.size()) {
        const std::size_t end = host.find('.', start);
        const std::size_t length = (end == std::string_view::npos ? host.size() : end) - start;
        if (length == 0 || length > 63) {
            return false;
        }
        const std::string_view label = host.substr(start, length);
        if (!isAsciiAlphaNumeric(static_cast<unsigned char>(label.front())) ||
            !isAsciiAlphaNumeric(static_cast<unsigned char>(label.back()))) {
            return false;
        }
        if (!std::all_of(label.begin(), label.end(), [](unsigned char character) {
                return isAsciiAlphaNumeric(character) || character == '-';
            })) {
            return false;
        }
        if (end == std::string_view::npos) {
            break;
        }
        start = end + 1;
    }
    const std::size_t lastDot = host.rfind('.');
    const std::string_view lastLabel = host.substr(lastDot == std::string_view::npos ? 0 : lastDot + 1);
    return !std::all_of(lastLabel.begin(), lastLabel.end(), [](unsigned char character) {
        return isAsciiDigit(character);
    });
}

std::wstring requiredArgument(std::string_view value, std::string_view field) {
    if (!hasSafeValue(value, 4096)) {
        throw std::invalid_argument(std::string(field) + " 参数无效");
    }
    return wideFromUtf8(value);
}

void validateStdin(const CliRequest& request) {
    if (request.command != CliCommand::RouterSave && request.command != CliCommand::Redial) {
        if (!request.stdinText.empty()) {
            throw std::invalid_argument("当前命令不接受标准输入");
        }
        return;
    }
    if (request.stdinText.empty() && request.command == CliCommand::Redial) {
        return;
    }
    if (request.stdinText.empty() || request.stdinText.size() > 1024) {
        throw std::invalid_argument("路由器密码不能为空或过长");
    }
    if (std::any_of(request.stdinText.begin(), request.stdinText.end(), [](unsigned char character) {
            return character == 0 || character == '\r' || character == '\n';
        })) {
        throw std::invalid_argument("路由器密码包含无效字符");
    }
}

void secureClear(std::string& value) {
    if (!value.empty()) {
        SecureZeroMemory(value.data(), value.size());
        value.clear();
    }
}

}

CliClient::CliClient(std::wstring executablePath) : executablePath_(std::move(executablePath)) {}

std::wstring CliClient::resolveExecutablePath() {
    std::wstring configured = environmentValue(L"NETWORKDOCTOR_CLI");
    if (!configured.empty()) {
        return std::filesystem::path(configured).lexically_normal();
    }

    std::vector<wchar_t> buffer(32768);
    DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0 || length >= buffer.size()) {
        return {};
    }

    std::wstring executablePath(buffer.data(), length);
    return std::filesystem::path(executablePath).parent_path() / L"NetworkDoctor.Cli.exe";
}

bool CliClient::isExpectedBackupPath(std::string_view backupPath) {
    std::wstring localAppData = environmentValue(L"LOCALAPPDATA");
    std::wstring candidate = wideFromUtf8(backupPath);
    if (localAppData.empty() || candidate.empty()) {
        return false;
    }

    std::filesystem::path candidatePath(candidate);
    if (!candidatePath.is_absolute()) {
        return false;
    }

    std::filesystem::path expected = std::filesystem::path(localAppData) / L"NetworkDoctor" / L"backup.json";
    return normalizedLower(candidatePath.lexically_normal().wstring()) ==
           normalizedLower(expected.lexically_normal().wstring());
}

bool CliClient::isRouterHost(std::string_view host) {
    return hasSafeValue(host, 253) && (isIpv4(host) || isDnsHost(host));
}

std::vector<std::wstring> CliClient::buildCommandLineArguments(const CliRequest& request) {
    std::vector<std::wstring> arguments;
    validateStdin(request);
    switch (request.command) {
        case CliCommand::Inspect:
            arguments.emplace_back(L"inspect");
            break;
        case CliCommand::Repair:
            arguments.emplace_back(L"repair");
            break;
        case CliCommand::Restore:
            if (!isExpectedBackupPath(request.backupPath)) {
                throw std::invalid_argument("备份路径无效，已拒绝执行撤销");
            }
            arguments.emplace_back(L"restore");
            arguments.push_back(requiredArgument(request.backupPath, "备份路径"));
            break;
        case CliCommand::Verify:
            arguments.emplace_back(L"verify");
            break;
        case CliCommand::Adapters:
            arguments.emplace_back(L"adapters");
            break;
        case CliCommand::PublicIp:
            arguments.emplace_back(L"public-ip");
            break;
        case CliCommand::IpStatic:
            arguments.emplace_back(L"ip-static");
            arguments.push_back(requiredArgument(request.adapter, "网卡"));
            arguments.push_back(requiredArgument(request.ip, "IP"));
            arguments.push_back(requiredArgument(request.mask, "子网掩码"));
            arguments.push_back(requiredArgument(request.gateway, "网关"));
            arguments.push_back(requiredArgument(request.dns1, "DNS"));
            arguments.push_back(requiredArgument(request.dns2, "备用 DNS"));
            break;
        case CliCommand::IpDhcp:
            arguments.emplace_back(L"ip-dhcp");
            arguments.push_back(requiredArgument(request.adapter, "网卡"));
            break;
        case CliCommand::RouterSave:
        case CliCommand::Redial:
            if (!isRouterHost(request.routerHost)) {
                throw std::invalid_argument("路由器地址只能是无路径的 IPv4 或 DNS 名称");
            }
            arguments.emplace_back(request.command == CliCommand::RouterSave ? L"router-save" : L"redial");
            arguments.push_back(requiredArgument(request.routerHost, "路由器地址"));
            break;
    }
    return arguments;
}

bool CliClient::isAvailable() const {
    if (executablePath_.empty()) {
        return false;
    }

    std::error_code error;
    std::filesystem::path path(executablePath_);
    return path.is_absolute() && std::filesystem::is_regular_file(path, error) && !error;
}

CliResult CliClient::run(CliCommand command) const {
    CliRequest request;
    request.command = command;
    if (command == CliCommand::Restore) {
        return {false, -1, {}, "撤销操作缺少已验证的备份路径"};
    }
    return run(request);
}

CliResult CliClient::run(const CliRequest& request) const {
    return runCommand(request);
}

CliResult CliClient::restore(std::string_view backupPath) const {
    CliRequest request;
    request.command = CliCommand::Restore;
    request.backupPath = backupPath;
    return run(request);
}

CliResult CliClient::runCommand(const CliRequest& request) const {
    if (!isAvailable()) {
        return {false, -1, {}, "未找到 NetworkDoctor.Cli.exe，请检查安装目录或 NETWORKDOCTOR_CLI"};
    }

    std::vector<std::wstring> arguments;
    try {
        arguments = buildCommandLineArguments(request);
    } catch (const std::exception& exception) {
        return {false, -1, {}, exception.what()};
    }

    std::wstring commandLine = quoteArgument(executablePath_);
    for (const std::wstring& argument : arguments) {
        commandLine.push_back(L' ');
        commandLine += quoteArgument(argument);
    }
    std::vector<wchar_t> mutableCommandLine(commandLine.begin(), commandLine.end());
    mutableCommandLine.push_back(L'\0');

    SECURITY_ATTRIBUTES security{};
    security.nLength = sizeof(security);
    security.bInheritHandle = TRUE;

    HANDLE inputRead = nullptr;
    HANDLE inputWrite = nullptr;
    HANDLE outputRead = nullptr;
    HANDLE outputWrite = nullptr;
    if (!CreatePipe(&inputRead, &inputWrite, &security, 0)) {
        return {false, -1, {}, windowsError("创建 CLI 输入管道失败", GetLastError())};
    }
    UniqueHandle childInput(inputRead);
    UniqueHandle parentInput(inputWrite);

    if (!CreatePipe(&outputRead, &outputWrite, &security, 0)) {
        return {false, -1, {}, windowsError("创建 CLI 输出管道失败", GetLastError())};
    }
    UniqueHandle parentOutput(outputRead);
    UniqueHandle childOutput(outputWrite);

    if (!SetHandleInformation(parentInput.get(), HANDLE_FLAG_INHERIT, 0) ||
        !SetHandleInformation(parentOutput.get(), HANDLE_FLAG_INHERIT, 0)) {
        return {false, -1, {}, windowsError("设置 CLI 管道继承失败", GetLastError())};
    }

    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESTDHANDLES;
    startup.hStdInput = childInput.get();
    startup.hStdOutput = childOutput.get();
    startup.hStdError = childOutput.get();

    PROCESS_INFORMATION process{};
    std::vector<wchar_t> applicationPath(executablePath_.begin(), executablePath_.end());
    applicationPath.push_back(L'\0');

    if (!CreateProcessW(
            applicationPath.data(),
            mutableCommandLine.data(),
            nullptr,
            nullptr,
            TRUE,
            CREATE_NO_WINDOW,
            nullptr,
            nullptr,
            &startup,
            &process)) {
        return {false, -1, {}, windowsError("启动 NetworkDoctor.Cli.exe 失败", GetLastError())};
    }

    UniqueHandle processHandle(process.hProcess);
    UniqueHandle threadHandle(process.hThread);
    childInput.reset();
    childOutput.reset();

    if (!request.stdinText.empty()) {
        std::string input = request.stdinText;
        input += "\r\n";
        DWORD written = 0;
        const DWORD expected = static_cast<DWORD>(input.size());
        const BOOL writeSucceeded = WriteFile(
            parentInput.get(),
            input.data(),
            expected,
            &written,
            nullptr);
        secureClear(input);
        if (!writeSucceeded || written != expected) {
            TerminateProcess(processHandle.get(), 1);
            WaitForSingleObject(processHandle.get(), 5000);
            return {false, -1, {}, "向 CLI 传递安全输入失败"};
        }
    }
    parentInput.reset();

    std::string output;
    std::array<char, 4096> buffer{};
    bool readSucceeded = true;
    DWORD bytesRead = 0;
    while (true) {
        if (!ReadFile(parentOutput.get(), buffer.data(), static_cast<DWORD>(buffer.size()), &bytesRead, nullptr)) {
            if (GetLastError() == ERROR_BROKEN_PIPE) {
                break;
            }
            readSucceeded = false;
            break;
        }
        if (bytesRead == 0) {
            break;
        }
        output.append(buffer.data(), bytesRead);
    }

    DWORD waitResult = WaitForSingleObject(processHandle.get(), 120000);
    if (waitResult != WAIT_OBJECT_0) {
        if (waitResult == WAIT_TIMEOUT) {
            TerminateProcess(processHandle.get(), 1);
            WaitForSingleObject(processHandle.get(), 5000);
        }
        return {false, -1, output, waitResult == WAIT_TIMEOUT ? "CLI 执行超时" : "等待 CLI 结束时失败"};
    }

    DWORD exitCode = 0;
    if (!GetExitCodeProcess(processHandle.get(), &exitCode)) {
        return {false, -1, output, windowsError("读取 CLI 退出码失败", GetLastError())};
    }

    if (!readSucceeded) {
        return {false, static_cast<int>(exitCode), output, "读取 CLI 输出失败"};
    }
    if (output.empty()) {
        return {false, static_cast<int>(exitCode), {}, "CLI 未返回 JSON"};
    }
    if (exitCode != 0) {
        return {false, static_cast<int>(exitCode), output, "CLI 执行失败"};
    }

    return {true, static_cast<int>(exitCode), output, {}};
}

}
#else

#include <utility>

namespace networkdoctor {

CliClient::CliClient(std::wstring executablePath) : executablePath_(std::move(executablePath)) {}

std::wstring CliClient::resolveExecutablePath() {
    return {};
}

bool CliClient::isExpectedBackupPath(std::string_view) {
    return false;
}

bool CliClient::isRouterHost(std::string_view) {
    return false;
}

std::vector<std::wstring> CliClient::buildCommandLineArguments(const CliRequest&) {
    return {};
}

bool CliClient::isAvailable() const {
    return false;
}

CliResult CliClient::run(CliCommand) const {
    return {false, -1, {}, "Network Doctor 原生界面仅支持 Windows"};
}

CliResult CliClient::run(const CliRequest&) const {
    return {false, -1, {}, "Network Doctor 原生界面仅支持 Windows"};
}

CliResult CliClient::restore(std::string_view) const {
    return {false, -1, {}, "Network Doctor 原生界面仅支持 Windows"};
}

CliResult CliClient::runCommand(const CliRequest&) const {
    return {false, -1, {}, "Network Doctor 原生界面仅支持 Windows"};
}

}
#endif
