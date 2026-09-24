namespace NetworkDoctor.Windows;

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;

public sealed class NetworkManagementService : IDisposable
{
    private static readonly string[] NoiseKeys =
    [
        "debug",
        "qos packet",
        "wfp native",
        "lightweight filter",
        "microsoft kernel",
        "teredo",
        "isatap",
        "6to4",
        "direct"
    ];

    private static readonly string[] Ipv4Endpoints =
    [
        "https://api.ipify.org",
        "http://ip.3322.net",
        "http://members.3322.org/dyndns/getip"
    ];

    private static readonly string[] Ipv6Endpoints =
    [
        "https://api6.ipify.org",
        "https://6.ipw.cn",
        "https://v6.ident.me/",
        "https://ipv6.icanhazip.com"
    ];

    private readonly INetworkAdapterProvider _adapterProvider;
    private readonly INetworkCommandRunner _commandRunner;
    private readonly HttpClient _httpClient;
    private readonly bool _disposeHandler;

    public NetworkManagementService()
        : this(
            new WindowsNetworkAdapterProvider(),
            new NetworkCommandRunner(),
            CreateDefaultHandler(),
            disposeHandler: true)
    {
    }

    internal NetworkManagementService(
        INetworkAdapterProvider adapterProvider,
        INetworkCommandRunner commandRunner,
        HttpMessageHandler handler)
        : this(adapterProvider, commandRunner, handler, disposeHandler: false)
    {
    }

    private NetworkManagementService(
        INetworkAdapterProvider adapterProvider,
        INetworkCommandRunner commandRunner,
        HttpMessageHandler handler,
        bool disposeHandler)
    {
        ArgumentNullException.ThrowIfNull(adapterProvider);
        ArgumentNullException.ThrowIfNull(commandRunner);
        ArgumentNullException.ThrowIfNull(handler);

        _adapterProvider = adapterProvider;
        _commandRunner = commandRunner;
        _httpClient = new HttpClient(handler, disposeHandler)
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        _disposeHandler = disposeHandler;
    }

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public IReadOnlyList<NetworkAdapterInfo> ListAdapters()
    {
        return _adapterProvider.GetAdapters()
            .Where(IsIncludedAdapter)
            .OrderByDescending(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .ThenByDescending(adapter => adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            .ThenBy(adapter => adapter.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(adapter => new NetworkAdapterInfo
            {
                Name = adapter.Name,
                Description = adapter.Description,
                Status = adapter.OperationalStatus.ToString(),
                LinkSpeed = FormatSpeed(adapter.Speed),
                IfIndex = adapter.IfIndex
            })
            .ToArray();
    }

    public NetworkAdapterDetail? GetDetail(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var adapter = _adapterProvider.GetAdapters()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Description, name, StringComparison.OrdinalIgnoreCase));
        if (adapter is null)
        {
            return null;
        }

        var ipv4 = adapter.Ipv4Address;
        var gateway = adapter.GatewayAddresses
            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)
            ?.ToString();
        var dnsServers = adapter.DnsAddresses
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => address.ToString())
            .ToArray();
        var ipv6 = adapter.Ipv6Addresses.FirstOrDefault(IsRoutableIpv6);

        return new NetworkAdapterDetail(
            adapter.Name,
            adapter.OperationalStatus.ToString(),
            FormatSpeed(adapter.Speed),
            ipv4?.ToString(),
            ipv4 is null ? null : GetPrefixFromMask(adapter.Ipv4Mask),
            ipv6?.ToString(),
            adapter.IsDhcpStateKnown && adapter.IsDhcpEnabled,
            string.IsNullOrWhiteSpace(gateway) ? null : gateway,
            dnsServers);
    }

    public NetworkOperationResult ApplyStatic(
        string adapter,
        string ip,
        string mask,
        string? gateway,
        string? dns1,
        string? dns2)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter);
        if (!ValidateIpv4(ip, out var ipError))
        {
            return new(false, ipError!);
        }
        if (!TryParsePrefix(mask, out var prefix))
        {
            return new(false, $"子网掩码格式不正确: {mask}");
        }
        if (!string.IsNullOrWhiteSpace(gateway) && !ValidateIpv4(gateway, out var gatewayError))
        {
            return new(false, gatewayError!);
        }
        if (!string.IsNullOrWhiteSpace(dns1) && !ValidateIpv4(dns1, out var dns1Error))
        {
            return new(false, dns1Error!);
        }
        if (!string.IsNullOrWhiteSpace(dns2) && !ValidateIpv4(dns2, out var dns2Error))
        {
            return new(false, dns2Error!);
        }

        var addressArguments = new List<string>
        {
            "interface",
            "ip",
            "set",
            "address",
            $"name={adapter}",
            "source=static",
            $"addr={ip}",
            $"mask={mask}"
        };
        if (!string.IsNullOrWhiteSpace(gateway))
        {
            addressArguments.Add($"gateway={gateway}");
            addressArguments.Add("gwmetric=1");
        }

        var addressResult = _commandRunner.Run(addressArguments);
        if (addressResult.ExitCode != 0)
        {
            return new(false, $"设置 IP 失败: {addressResult.Output}");
        }

        if (!string.IsNullOrWhiteSpace(dns1))
        {
            var dnsResult = _commandRunner.Run(
            [
                "interface",
                "ip",
                "set",
                "dns",
                $"name={adapter}",
                "source=static",
                $"addr={dns1}",
                "validate=no"
            ]);
            if (dnsResult.ExitCode != 0)
            {
                return new(false, $"设置 DNS 失败: {dnsResult.Output}");
            }
        }

        if (!string.IsNullOrWhiteSpace(dns2))
        {
            var dnsResult = _commandRunner.Run(
            [
                "interface",
                "ip",
                "add",
                "dns",
                $"name={adapter}",
                $"addr={dns2}",
                "index=2",
                "validate=no"
            ]);
            if (dnsResult.ExitCode != 0)
            {
                return new(false, $"设置备用 DNS 失败: {dnsResult.Output}");
            }
        }

        return new(true, $"[{adapter}] 已设置为 {ip}/{prefix}");
    }

    public NetworkOperationResult ApplyDhcp(string adapter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter);
        var addressResult = _commandRunner.Run(
        [
            "interface",
            "ip",
            "set",
            "address",
            $"name={adapter}",
            "source=dhcp"
        ]);
        if (addressResult.ExitCode != 0)
        {
            return new(false, $"切回 DHCP 失败: {addressResult.Output}");
        }

        var dnsResult = _commandRunner.Run(
        [
            "interface",
            "ip",
            "set",
            "dns",
            $"name={adapter}",
            "source=dhcp"
        ]);
        return dnsResult.ExitCode == 0
            ? new(true, $"[{adapter}] 已切换为 DHCP（IP + DNS）")
            : new(false, $"[{adapter}] IP 已切 DHCP，但 DNS 切换失败: {dnsResult.Output}");
    }

    public async Task<string?> GetPublicIpAsync(
        AddressFamily family,
        CancellationToken cancellationToken = default)
    {
        var endpoints = family == AddressFamily.InterNetwork ? Ipv4Endpoints : Ipv6Endpoints;
        foreach (var endpoint in endpoints)
        {
            try
            {
                var value = (await _httpClient.GetStringAsync(endpoint, cancellationToken)).Trim();
                if (value.Length > 40 ||
                    !IPAddress.TryParse(value, out var address) ||
                    address.AddressFamily != family)
                {
                    continue;
                }

                return address.ToString();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }
        }

        return null;
    }

    public async Task<string?> GetPublicIpAsync(CancellationToken cancellationToken = default)
    {
        return await GetPublicIpAsync(AddressFamily.InterNetwork, cancellationToken);
    }

    public async Task<PublicIpAddresses> GetPublicIpsAsync(
        CancellationToken cancellationToken = default)
    {
        var ipv4 = await GetPublicIpAsync(AddressFamily.InterNetwork, cancellationToken);
        var ipv6 = await GetPublicIpAsync(AddressFamily.InterNetworkV6, cancellationToken);
        return new(ipv4, ipv6);
    }

    internal static ProcessStartInfo CreateNetshStartInfo(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var startInfo = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        if (_disposeHandler)
        {
            (_adapterProvider as IDisposable)?.Dispose();
        }
    }

    private static HttpMessageHandler CreateDefaultHandler()
    {
        return new HttpClientHandler
        {
            UseProxy = false
        };
    }

    private static bool IsIncludedAdapter(NetworkAdapterSnapshot adapter)
    {
        if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
        {
            return false;
        }

        var searchable = $"{adapter.Name} {adapter.Description}".ToLowerInvariant();
        return NoiseKeys.All(key => !searchable.Contains(key, StringComparison.Ordinal));
    }

    private static bool IsRoutableIpv6(IPAddress address)
    {
        if (!address.AddressFamily.Equals(AddressFamily.InterNetworkV6))
        {
            return false;
        }

        if (address.IsIPv6LinkLocal || address.Equals(IPAddress.IPv6Loopback))
        {
            return false;
        }

        return !address.ToString().StartsWith("::1", StringComparison.Ordinal);
    }

    private static string FormatSpeed(long bitsPerSecond)
    {
        if (bitsPerSecond <= 0)
        {
            return "0 bps";
        }
        if (bitsPerSecond >= 1_000_000_000d)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{bitsPerSecond / 1_000_000_000d:0.#} Gbps");
        }
        if (bitsPerSecond >= 1_000_000d)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{bitsPerSecond / 1_000_000d:0.#} Mbps");
        }
        if (bitsPerSecond >= 1_000d)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{bitsPerSecond / 1_000d:0.#} Kbps");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{bitsPerSecond} bps");
    }

    private static int? GetPrefixFromMask(IPAddress? mask)
    {
        if (mask is null || mask.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

        var prefix = 0;
        foreach (var value in mask.GetAddressBytes())
        {
            prefix += System.Numerics.BitOperations.PopCount((uint)value);
        }

        return prefix;
    }

    private static bool ValidateIpv4(string value, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "IP 地址不能为空";
            return false;
        }
        if (!IPAddress.TryParse(value, out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork)
        {
            error = $"IP 地址格式不正确: {value}";
            return false;
        }

        return true;
    }

    private static bool TryParsePrefix(string mask, out int prefix)
    {
        prefix = 0;
        if (!IPAddress.TryParse(mask, out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var previousBit = 1;
        foreach (var value in address.GetAddressBytes())
        {
            for (var bit = 7; bit >= 0; bit--)
            {
                var currentBit = (value >> bit) & 1;
                if (currentBit == 1)
                {
                    if (previousBit == 0)
                    {
                        return false;
                    }

                    prefix++;
                }
                else
                {
                    previousBit = 0;
                }
            }
        }

        return true;
    }
}

internal interface INetworkAdapterProvider
{
    IReadOnlyList<NetworkAdapterSnapshot> GetAdapters();
}

internal interface INetworkCommandRunner
{
    NetworkCommandResult Run(IReadOnlyList<string> arguments);
}

internal sealed record NetworkCommandResult(int ExitCode, string Output);

internal sealed class NetworkCommandRunner : INetworkCommandRunner
{
    public NetworkCommandResult Run(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var startInfo = NetworkManagementService.CreateNetshStartInfo(arguments);
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("无法启动 netsh.exe");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new(process.ExitCode, (standardOutput + standardError).Trim());
    }
}

internal sealed class WindowsNetworkAdapterProvider : INetworkAdapterProvider
{
    public IReadOnlyList<NetworkAdapterSnapshot> GetAdapters()
    {
        var adapters = new List<NetworkAdapterSnapshot>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            var properties = adapter.GetIPProperties();
            var ipv4Properties = properties.GetIPv4Properties();
            adapters.Add(new NetworkAdapterSnapshot
            {
                Name = adapter.Name,
                Description = adapter.Description,
                NetworkInterfaceType = adapter.NetworkInterfaceType,
                OperationalStatus = adapter.OperationalStatus,
                Speed = adapter.Speed,
                IfIndex = ipv4Properties?.Index ?? 0,
                Ipv4Address = properties.UnicastAddresses
                    .FirstOrDefault(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    ?.Address,
                Ipv4Mask = properties.UnicastAddresses
                    .FirstOrDefault(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    ?.IPv4Mask,
                Ipv6Addresses = properties.UnicastAddresses
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    .Select(address => address.Address)
                    .ToArray(),
                GatewayAddresses = properties.GatewayAddresses
                    .Select(gateway => gateway.Address)
                    .ToArray(),
                DnsAddresses = properties.DnsAddresses.ToArray(),
                IsDhcpStateKnown = ipv4Properties is not null,
                IsDhcpEnabled = ipv4Properties?.IsDhcpEnabled ?? false
            });
        }

        return adapters;
    }
}

internal sealed record NetworkAdapterSnapshot
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public NetworkInterfaceType NetworkInterfaceType { get; init; }
    public OperationalStatus OperationalStatus { get; init; }
    public long Speed { get; init; }
    public int IfIndex { get; init; }
    public IPAddress? Ipv4Address { get; init; }
    public IPAddress? Ipv4Mask { get; init; }
    public IReadOnlyList<IPAddress> Ipv6Addresses { get; init; } = [];
    public IReadOnlyList<IPAddress> GatewayAddresses { get; init; } = [];
    public IReadOnlyList<IPAddress> DnsAddresses { get; init; } = [];
    public bool IsDhcpStateKnown { get; init; }
    public bool IsDhcpEnabled { get; init; }
}
