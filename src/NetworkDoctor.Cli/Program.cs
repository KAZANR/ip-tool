namespace NetworkDoctor.Cli;

using System.Net;
using System.Text.Json;
using NetworkDoctor.Windows;

public enum CliCommand
{
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
}

public sealed record CliCommandRequest(
    CliCommand Command,
    string? BackupFile = null,
    string? Adapter = null,
    string? Ip = null,
    string? Mask = null,
    string? Gateway = null,
    string? Dns1 = null,
    string? Dns2 = null,
    string? Host = null);

internal interface IRouterCommandService : IDisposable
{
    void SavePassword(string password);

    string? LoadPassword();

    Task<string> LoginAsync(string password, CancellationToken cancellationToken = default);

    Task<(string? User, string? Password)> GetPppoeCredentialsAsync(
        string token,
        CancellationToken cancellationToken = default);

    Task RedialPppoeAsync(
        string token,
        string user,
        string password,
        CancellationToken cancellationToken = default);
}

public static class CliContract
{
    public static CliCommandRequest Classify(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args switch
        {
            ["inspect"] => new(CliCommand.Inspect),
            ["repair"] => new(CliCommand.Repair),
            ["verify"] => new(CliCommand.Verify),
            ["adapters"] => new(CliCommand.Adapters),
            ["public-ip"] => new(CliCommand.PublicIp),
            ["restore", var backupFile] when HasText(backupFile) => new(CliCommand.Restore, backupFile),
            ["ip-dhcp", var adapter] when HasText(adapter) => new(CliCommand.IpDhcp, Adapter: adapter),
            ["router-save", var host] when IsRouterHost(host) => new(CliCommand.RouterSave, Host: host),
            ["redial", var host] when IsRouterHost(host) => new(CliCommand.Redial, Host: host),
            ["ip-static", var adapter, var ip, var mask, var gateway, var dns1, var dns2]
                when HasText(adapter) && HasText(ip) && HasText(mask) && HasText(gateway) && HasText(dns1) && HasText(dns2) =>
                new(CliCommand.IpStatic, Adapter: adapter, Ip: ip, Mask: mask, Gateway: gateway, Dns1: dns1, Dns2: dns2),
            _ => throw new ArgumentException("参数无效")
        };
    }

    public static string SerializeInspect(IReadOnlyList<NetworkDoctor.Core.ProxyFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var score = Math.Max(0, 100 - findings.Count * 10);
        var summary = findings.Count == 0
            ? "未发现活动代理设置"
            : $"发现 {findings.Count} 个活动代理设置";

        return JsonSerializer.Serialize(new
        {
            ok = true,
            findings,
            score,
            summary
        });
    }

    public static string SerializeRepair(ProxyRepairResult result, string backupFile)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFile);

        var ok = result.Items.All(item => item.Status != ProxyRepairStatus.Failed);
        return JsonSerializer.Serialize(new
        {
            ok,
            backup = backupFile,
            items = result.Items,
            summary = ok ? "代理修复完成" : "代理修复部分失败"
        });
    }

    public static string SerializeRestore(ProxyRepairResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var ok = result.Items.All(item => item.Status != ProxyRepairStatus.Failed);
        return JsonSerializer.Serialize(new
        {
            ok,
            items = result.Items,
            summary = ok ? "代理设置已恢复" : "代理设置恢复部分失败"
        });
    }

    public static string SerializeVerify(ConnectivityResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var summary = result.IsConnected
            ? "网络连接验证成功"
            : result.ErrorSummary ?? "网络连接验证失败";
        return JsonSerializer.Serialize(new
        {
            ok = result.IsConnected,
            summary
        });
    }

    public static string SerializeAdapters(
        IReadOnlyList<NetworkAdapterInfo> adapters,
        IReadOnlyList<NetworkAdapterDetail?>? details = null)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        return JsonSerializer.Serialize(new
        {
            ok = true,
            command = "adapters",
            adapters = adapters.Select((adapter, index) =>
            {
                var detail = details is not null && index < details.Count ? details[index] : null;
                return new
                {
                    adapter.Name,
                    adapter.Description,
                    adapter.Status,
                    adapter.LinkSpeed,
                    adapter.IfIndex,
                    Ipv4 = detail?.Ipv4,
                    PrefixLength = detail?.PrefixLength,
                    Ipv6 = detail?.Ipv6,
                    IsDhcp = detail?.IsDhcp ?? false,
                    Gateway = detail?.Gateway,
                    DnsServers = detail?.DnsServers ?? Array.Empty<string>()
                };
            }),
            summary = $"找到 {adapters.Count} 个网络适配器"
        });
    }

    public static string SerializePublicIp(PublicIpAddresses addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var ok = !string.IsNullOrWhiteSpace(addresses.Ipv4) || !string.IsNullOrWhiteSpace(addresses.Ipv6);
        return JsonSerializer.Serialize(new
        {
            ok,
            command = "public-ip",
            ipv4 = addresses.Ipv4,
            ipv6 = addresses.Ipv6,
            summary = ok ? "公网 IP 检测成功" : "未检测到公网 IP"
        });
    }

    public static string SerializeNetworkOperation(
        CliCommand command,
        string adapter,
        NetworkOperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter);
        if (command is not (CliCommand.IpStatic or CliCommand.IpDhcp))
        {
            throw new ArgumentException("命令无效", nameof(command));
        }

        return JsonSerializer.Serialize(new
        {
            ok = result.Success,
            command = ToCommandName(command),
            adapter,
            message = result.Message,
            summary = result.Message
        });
    }

    public static string SerializeRouterSave(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        return JsonSerializer.Serialize(new
        {
            ok = true,
            command = "router-save",
            host,
            summary = "路由器密码已保存"
        });
    }

    public static string SerializeRedial(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        return JsonSerializer.Serialize(new
        {
            ok = true,
            command = "redial",
            host,
            summary = "路由器 PPPoE 重拨完成"
        });
    }

    public static string SerializeError(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return JsonSerializer.Serialize(new
        {
            ok = false,
            error = message
        });
    }

    private static bool HasText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool IsRouterHost(string? host)
    {
        if (host is null ||
            !HasText(host) ||
            host.Length > 253 ||
            host.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) ||
            host.Contains('/') ||
            host.Contains('\\') ||
            host.Contains(':') ||
            host.StartsWith('.') ||
            host.EndsWith('.'))
        {
            return false;
        }

        if (Uri.CheckHostName(host) == UriHostNameType.IPv4)
        {
            return IPAddress.TryParse(host, out var address) &&
                address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
        }

        var labels = host.Split('.');
        return labels.All(label =>
                label.Length is >= 1 and <= 63 &&
                char.IsAsciiLetterOrDigit(label[0]) &&
                char.IsAsciiLetterOrDigit(label[^1]) &&
                label.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')) &&
            !labels[^1].All(char.IsAsciiDigit);
    }

    private static string ToCommandName(CliCommand command)
    {
        return command switch
        {
            CliCommand.IpStatic => "ip-static",
            CliCommand.IpDhcp => "ip-dhcp",
            _ => throw new ArgumentException("命令无效", nameof(command))
        };
    }
}

public static class Program
{
    public static int Main(string[] args)
    {
        return RunAsync(args, Console.In, Console.Out, Console.Error).GetAwaiter().GetResult();
    }

    internal static async Task<int> RunAsync(
        string[] args,
        TextReader input,
        TextWriter output,
        TextWriter error,
        Func<string, IRouterCommandService>? routerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            var request = CliContract.Classify(args);
            return request.Command switch
            {
                CliCommand.Inspect => RunInspect(output),
                CliCommand.Repair => await RunRepair(output),
                CliCommand.Restore => await RunRestore(request.BackupFile!, output),
                CliCommand.Verify => await RunVerify(output),
                CliCommand.Adapters => RunAdapters(output),
                CliCommand.PublicIp => await RunPublicIp(output),
                CliCommand.IpStatic => RunIpStatic(request, output),
                CliCommand.IpDhcp => RunIpDhcp(request, output),
                CliCommand.RouterSave => RunRouterSave(request.Host!, input, output, routerFactory),
                CliCommand.Redial => await RunRedial(request.Host!, input, output, routerFactory),
                _ => throw new ArgumentException("参数无效")
            };
        }
        catch (Exception exception)
        {
            error.WriteLine(CliContract.SerializeError(exception.Message));
            return 1;
        }
    }

    private static int RunInspect(TextWriter output)
    {
        var findings = new WindowsProxyInspector().Inspect();
        output.WriteLine(CliContract.SerializeInspect(findings));
        return 0;
    }

    private static async Task<int> RunRepair(TextWriter output)
    {
        var result = await new WindowsProxyRepairer().RepairAsync();
        var backupFile = GetBackupFile();
        var directory = Path.GetDirectoryName(backupFile)!;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(backupFile, JsonSerializer.Serialize(result.Backup));
        output.WriteLine(CliContract.SerializeRepair(result, backupFile));
        return 0;
    }

    private static async Task<int> RunRestore(string backupFile, TextWriter output)
    {
        if (!File.Exists(backupFile))
        {
            throw new FileNotFoundException("备份文件不存在", backupFile);
        }

        var backup = JsonSerializer.Deserialize<ProxyBackup>(await File.ReadAllTextAsync(backupFile));
        if (backup is null)
        {
            throw new InvalidDataException("备份文件无效");
        }

        var result = await new WindowsProxyRepairer().RestoreAsync(backup);
        output.WriteLine(CliContract.SerializeRestore(result));
        return 0;
    }

    private static async Task<int> RunVerify(TextWriter output)
    {
        using var verifier = new WindowsConnectivityVerifier();
        var result = await verifier.VerifyAsync();
        output.WriteLine(CliContract.SerializeVerify(result));
        return 0;
    }

    private static int RunAdapters(TextWriter output)
    {
        using var service = new NetworkManagementService();
        var adapters = service.ListAdapters();
        var details = adapters.Select(adapter => service.GetDetail(adapter.Name)).ToArray();
        output.WriteLine(CliContract.SerializeAdapters(adapters, details));
        return 0;
    }

    private static async Task<int> RunPublicIp(TextWriter output)
    {
        using var service = new NetworkManagementService();
        var addresses = await service.GetPublicIpsAsync();
        output.WriteLine(CliContract.SerializePublicIp(addresses));
        return 0;
    }

    private static int RunIpStatic(CliCommandRequest request, TextWriter output)
    {
        using var service = new NetworkManagementService();
        var result = service.ApplyStatic(
            request.Adapter!,
            request.Ip!,
            request.Mask!,
            request.Gateway,
            request.Dns1,
            request.Dns2);
        output.WriteLine(CliContract.SerializeNetworkOperation(request.Command, request.Adapter!, result));
        return result.Success ? 0 : 1;
    }

    private static int RunIpDhcp(CliCommandRequest request, TextWriter output)
    {
        using var service = new NetworkManagementService();
        var result = service.ApplyDhcp(request.Adapter!);
        output.WriteLine(CliContract.SerializeNetworkOperation(request.Command, request.Adapter!, result));
        return result.Success ? 0 : 1;
    }

    private static int RunRouterSave(
        string host,
        TextReader input,
        TextWriter output,
        Func<string, IRouterCommandService>? routerFactory)
    {
        try
        {
            var password = input.ReadLine();
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("路由器密码不能为空");
            }

            using var router = (routerFactory ?? (value => new RouterCommandService(value)))(host);
            router.SavePassword(password);
            output.WriteLine(CliContract.SerializeRouterSave(host));
            return 0;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("路由器密码保存失败");
        }
    }

    private static async Task<int> RunRedial(
        string host,
        TextReader input,
        TextWriter output,
        Func<string, IRouterCommandService>? routerFactory)
    {
        try
        {
            using var router = (routerFactory ?? (value => new RouterCommandService(value)))(host);
            var password = input.ReadLine();
            var passwordWasProvided = !string.IsNullOrWhiteSpace(password);
            if (!passwordWasProvided)
            {
                password = router.LoadPassword();
            }
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("路由器密码不能为空");
            }

            if (passwordWasProvided)
            {
                router.SavePassword(password);
            }
            var token = await router.LoginAsync(password);
            var (user, pppoePassword) = await router.GetPppoeCredentialsAsync(token);
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pppoePassword))
            {
                throw new InvalidOperationException("路由器未返回 PPPoE 凭据");
            }

            await router.RedialPppoeAsync(token, user, pppoePassword);
            output.WriteLine(CliContract.SerializeRedial(host));
            return 0;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("路由器重拨失败");
        }
    }

    private static string GetBackupFile()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("无法确定本地应用数据目录");
        }

        return Path.Combine(localAppData, "NetworkDoctor", "backup.json");
    }

    private sealed class RouterCommandService : IRouterCommandService
    {
        private readonly MiRouterService _service;

        public RouterCommandService(string host)
        {
            _service = new MiRouterService(host);
        }

        public void SavePassword(string password)
        {
            _service.SavePassword(password);
        }

        public string? LoadPassword()
        {
            return _service.LoadPassword();
        }

        public Task<string> LoginAsync(string password, CancellationToken cancellationToken = default)
        {
            return _service.LoginAsync(password, cancellationToken);
        }

        public Task<(string? User, string? Password)> GetPppoeCredentialsAsync(
            string token,
            CancellationToken cancellationToken = default)
        {
            return _service.GetPppoeCredentialsAsync(token, cancellationToken);
        }

        public Task RedialPppoeAsync(
            string token,
            string user,
            string password,
            CancellationToken cancellationToken = default)
        {
            return _service.RedialPppoeAsync(token, user, password, cancellationToken);
        }

        public void Dispose()
        {
            _service.Dispose();
        }
    }
}
