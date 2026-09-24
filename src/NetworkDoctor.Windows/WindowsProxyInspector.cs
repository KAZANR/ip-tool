namespace NetworkDoctor.Windows;

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using NetworkDoctor.Core;

public sealed class WindowsProxyInspector : IProxyInspector
{
    private const string InternetSettingsPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    public IReadOnlyList<ProxyFinding> Inspect()
    {
        return ReadWinInetSettings()
            .Concat(ReadWinHttpSettings())
            .Concat(ParseProxyEnvironment(Environment.GetEnvironmentVariable))
            .ToArray();
    }

    internal static IReadOnlyList<ProxyFinding> ParseWinInetSettings(
        int? proxyEnable,
        string? proxyServer,
        string? autoConfigUrl)
    {
        var findings = new List<ProxyFinding>();

        if (proxyEnable == 1 && !string.IsNullOrWhiteSpace(proxyServer))
        {
            findings.Add(new ProxyFinding(
                ProxySource.WinInet,
                "System",
                "ProxyServer",
                proxyServer.Trim(),
                "系统和应用可能通过此代理访问网络"));
        }

        if (!string.IsNullOrWhiteSpace(autoConfigUrl))
        {
            findings.Add(new ProxyFinding(
                ProxySource.WinInet,
                "System",
                "AutoConfigURL",
                autoConfigUrl.Trim(),
                "系统可能通过自动配置脚本使用代理"));
        }

        return findings;
    }

    internal static IReadOnlyList<ProxyFinding> ParseWinHttpOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                continue;
            }

            var name = line[..separatorIndex].Trim();
            var address = line[(separatorIndex + 1)..].Trim();
            if ((name == "WINHTTP_PROXY_NAME" || name == "代理服务器名称")
                && !string.IsNullOrWhiteSpace(address))
            {
                return
                [
                    new ProxyFinding(
                        ProxySource.WinHttp,
                        "Machine",
                        "WINHTTP_PROXY_NAME",
                        address,
                        "系统服务可能通过此代理访问网络")
                ];
            }
        }

        return [];
    }

    internal static IReadOnlyList<ProxyFinding> ParseProxyEnvironment(Func<string, string?> readEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);

        var findings = new List<ProxyFinding>();
        foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY" })
        {
            var address = readEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(address))
            {
                continue;
            }

            findings.Add(new ProxyFinding(
                ProxySource.Environment,
                "Process",
                name,
                address.Trim(),
                "当前进程及其子进程可能使用此代理配置"));
        }

        return findings;
    }

    private static IReadOnlyList<ProxyFinding> ReadWinInetSettings()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: false);
            if (key is null)
            {
                return [];
            }

            return ParseWinInetSettings(
                ReadInt32(key.GetValue("ProxyEnable")),
                key.GetValue("ProxyServer") as string,
                key.GetValue("AutoConfigURL") as string);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (System.Security.SecurityException)
        {
            return [];
        }
    }

    private static IReadOnlyList<ProxyFinding> ReadWinHttpSettings()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("winhttp");
        startInfo.ArgumentList.Add("show");
        startInfo.ArgumentList.Add("proxy");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return ParseWinHttpOutput(output);
        }
        catch (Win32Exception)
        {
            return [];
        }
        catch (InvalidOperationException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static int? ReadInt32(object? value)
    {
        return value switch
        {
            int number => number,
            string text when int.TryParse(text, out var number) => number,
            _ => null
        };
    }
}
