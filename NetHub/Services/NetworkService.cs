using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetHub.Models;

namespace NetHub.Services;

public static class NetworkService
{
    public static bool IsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    public static IReadOnlyList<AdapterInfo> ListAdapters()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n =>
                n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                && !IsNoiseAdapter(n.Name, n.Description))
            .OrderByDescending(n => n.OperationalStatus == OperationalStatus.Up)
            .ThenByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            .ThenBy(n => n.Name)
            .Select(n => new AdapterInfo
            {
                Name = n.Name,
                Description = n.Description,
                Status = n.OperationalStatus.ToString(),
                LinkSpeed = FormatSpeed(n.Speed),
                IfIndex = GetIfIndex(n)
            })
            .ToList();
    }

    private static bool IsNoiseAdapter(string name, string description)
    {
        var s = $"{name} {description}".ToLowerInvariant();
        string[] keys =
        {
            "debug", "qos packet", "wfp native", "lightweight filter",
            "microsoft kernel", "teredo", "isatap", "6to4", "direct"
        };
        return keys.Any(k => s.Contains(k, StringComparison.Ordinal));
    }

    public static AdapterDetail? GetDetail(string name)
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(n.Description, name, StringComparison.OrdinalIgnoreCase));
        if (nic is null) return null;

        var props = nic.GetIPProperties();
        var v4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
        var gw = props.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address?.ToString();
        var dns = props.DnsAddresses
            .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
            .Select(d => d.ToString())
            .ToList();

        // 某些虚拟/未绑定 IPv4 的网卡调用 GetIPv4Properties 会抛 10043
        var isDhcp = false;
        var dhcpKnown = false;
        try
        {
            var ip4 = props.GetIPv4Properties();
            if (ip4 is not null)
            {
                isDhcp = ip4.IsDhcpEnabled;
                dhcpKnown = true;
            }
        }
        catch (NetworkInformationException)
        {
            dhcpKnown = false;
        }

        // 有 IPv4 但读不到 DHCP 属性时，用地址前缀/来源猜测：常见 DHCP 为 /24 + 有网关
        if (!dhcpKnown && v4 is not null)
        {
            isDhcp = false; // 无法确认时保守显示静态，避免误导
        }

        // 本机 IPv6：跳过链路本地 fe80::，取全局/ULA
        var v6 = props.UnicastAddresses
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6)
            .Select(a => a.Address)
            .FirstOrDefault(a =>
            {
                var s = a.ToString();
                if (s.StartsWith("fe80", StringComparison.OrdinalIgnoreCase)) return false;
                if (s.StartsWith("::1", StringComparison.Ordinal)) return false;
                return true;
            });

        return new AdapterDetail(
            nic.Name,
            nic.OperationalStatus.ToString(),
            FormatSpeed(nic.Speed),
            v4?.Address.ToString(),
            v4 is null ? null : GetPrefixFromMask(v4.IPv4Mask),
            v6?.ToString(),
            isDhcp,
            string.IsNullOrWhiteSpace(gw) ? null : gw,
            dns);
    }

    public static OperationResult ApplyStatic(string adapter, string ip, string mask, string? gateway, string? dns1, string? dns2)
    {
        if (!ValidateIp(ip, out var ipErr)) return new(false, ipErr!);
        if (!TryPrefix(mask, out var prefix)) return new(false, $"子网掩码格式不正确: {mask}");
        if (!string.IsNullOrWhiteSpace(gateway) && !ValidateIp(gateway, out var gwErr)) return new(false, gwErr!);
        if (!string.IsNullOrWhiteSpace(dns1) && !ValidateIp(dns1, out var d1)) return new(false, d1!);
        if (!string.IsNullOrWhiteSpace(dns2) && !ValidateIp(dns2, out var d2)) return new(false, d2!);

        var name = Quote(adapter);
        var addrArgs = string.IsNullOrWhiteSpace(gateway)
            ? $"interface ip set address name={name} source=static addr={ip} mask={mask}"
            : $"interface ip set address name={name} source=static addr={ip} mask={mask} gateway={gateway} gwmetric=1";

        var r1 = RunNetsh(addrArgs);
        if (r1.ExitCode != 0)
            return new(false, $"设置 IP 失败: {r1.Output}");

        if (!string.IsNullOrWhiteSpace(dns1))
        {
            var r2 = RunNetsh($"interface ip set dns name={name} source=static addr={dns1} validate=no");
            if (r2.ExitCode != 0)
                return new(false, $"设置 DNS 失败: {r2.Output}");

            if (!string.IsNullOrWhiteSpace(dns2))
            {
                var r3 = RunNetsh($"interface ip add dns name={name} addr={dns2} index=2 validate=no");
                if (r3.ExitCode != 0)
                    return new(false, $"设置备用 DNS 失败: {r3.Output}");
            }
        }

        return new(true, $"[{adapter}] 已设置为 {ip}/{prefix}");
    }

    public static OperationResult ApplyDhcp(string adapter)
    {
        var name = Quote(adapter);
        var r1 = RunNetsh($"interface ip set address name={name} source=dhcp");
        if (r1.ExitCode != 0)
            return new(false, $"切回 DHCP 失败: {r1.Output}");

        var r2 = RunNetsh($"interface ip set dns name={name} source=dhcp");
        var msg = r2.ExitCode == 0
            ? $"[{adapter}] 已切换为 DHCP（IP + DNS）"
            : $"[{adapter}] IP 已切 DHCP，但 DNS 切换失败: {r2.Output}";
        return new(r2.ExitCode == 0, msg);
    }

    public static async Task<string?> GetPublicIpAsync(CancellationToken ct = default)
        => await GetPublicIpAsync(AddressFamily.InterNetwork, ct);

    public static async Task<string?> GetPublicIpAsync(AddressFamily family, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        // IPv4 / IPv6 各自多源兜底
        string[] urls = family == AddressFamily.InterNetwork
            ? new[]
            {
                "https://api.ipify.org",
                "http://ip.3322.net",
                "http://members.3322.org/dyndns/getip"
            }
            : new[]
            {
                "https://api6.ipify.org",
                "https://6.ipw.cn",
                "https://v6.ident.me/",
                "https://ipv6.icanhazip.com"
            };

        foreach (var url in urls)
        {
            try
            {
                var s = (await http.GetStringAsync(url, ct)).Trim();
                if (s.Length > 40) continue; // IPv6 最长约 39
                if (!IPAddress.TryParse(s, out var addr)) continue;
                if (addr.AddressFamily != family) continue;
                return addr.ToString();
            }
            catch
            {
                // try next
            }
        }
        return null;
    }

    public static async Task<(string? V4, string? V6)> GetPublicIpsAsync(CancellationToken ct = default)
    {
        var v4 = await GetPublicIpAsync(AddressFamily.InterNetwork, ct);
        var v6 = await GetPublicIpAsync(AddressFamily.InterNetworkV6, ct);
        return (v4, v6);
    }

    private static bool ValidateIpAny(string text)
        => IPAddress.TryParse(text, out _);

    private static (int ExitCode, string Output) RunNetsh(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, (stdout + stderr).Trim());
    }

    private static string Quote(string s) => $"\"{s}\"";

    private static string FormatSpeed(long bps)
    {
        if (bps <= 0) return "0 bps";
        if (bps >= 1_000_000_000d) return $"{bps / 1_000_000_000d:0.#} Gbps";
        if (bps >= 1_000_000d) return $"{bps / 1_000_000d:0.#} Mbps";
        if (bps >= 1_000d) return $"{bps / 1_000d:0.#} Kbps";
        return $"{bps} bps";
    }

    private static int GetIfIndex(NetworkInterface nic)
    {
        try
        {
            var props = nic.GetIPProperties();
            var index = props.GetIPv4Properties()?.Index;
            return index ?? 0;
        }
        catch (NetworkInformationException)
        {
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private static int? GetPrefixFromMask(IPAddress? mask)
    {
        if (mask is null) return null;
        var bytes = mask.GetAddressBytes();
        var bits = 0;
        foreach (var b in bytes)
        {
            for (var i = 7; i >= 0; i--)
            {
                if (((b >> i) & 1) == 1) bits++;
                else return bits;
            }
        }
        return bits;
    }

    private static bool ValidateIp(string text, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "IP 地址不能为空";
            return false;
        }
        if (!IPAddress.TryParse(text, out var addr) || addr.AddressFamily != AddressFamily.InterNetwork)
        {
            error = $"IP 地址格式不正确: {text}";
            return false;
        }
        return true;
    }

    private static bool TryPrefix(string mask, out int prefix)
    {
        prefix = -1;
        if (!IPAddress.TryParse(mask, out var addr) || addr.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var total = 0;
        foreach (var b in addr.GetAddressBytes())
        {
            if (b == 0) continue;
            var high = Convert.ToString(b, 2);
            if (high.Contains("01", StringComparison.Ordinal)) return false;
            total += high.Replace("0", string.Empty).Length;
        }
        prefix = total;
        return true;
    }

    public static string PrefixToMask(int prefix)
    {
        var bytes = new byte[4];
        var bits = prefix;
        for (var i = 0; i < 4; i++)
        {
            if (bits >= 8) { bytes[i] = 255; bits -= 8; }
            else if (bits > 0) { bytes[i] = (byte)(256 - Math.Pow(2, 8 - bits)); bits = 0; }
            else bytes[i] = 0;
        }
        return string.Join(".", bytes);
    }
}
