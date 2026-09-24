namespace NetworkDoctor.Windows;

using System.Net;
using Microsoft.Win32;

public sealed class WindowsConnectivityVerifier : IConnectivityVerifier, IDisposable
{
    private static readonly Uri ConnectivityEndpoint =
        new("https://www.baidu.com/");

    private readonly HttpClient _httpClient;

    public WindowsConnectivityVerifier()
        : this(CreateDefaultHandler(), TimeSpan.FromSeconds(8), disposeHandler: true)
    {
    }

    internal WindowsConnectivityVerifier(HttpMessageHandler handler)
        : this(handler, TimeSpan.FromSeconds(8), disposeHandler: false)
    {
    }

    internal WindowsConnectivityVerifier(HttpMessageHandler handler, TimeSpan timeout)
        : this(handler, timeout, disposeHandler: false)
    {
    }

    private WindowsConnectivityVerifier(
        HttpMessageHandler handler,
        TimeSpan timeout,
        bool disposeHandler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _httpClient = new HttpClient(handler, disposeHandler)
        {
            Timeout = timeout
        };
    }

    internal static HttpClientHandler CreateHandler(int? proxyEnable, string? proxyServer)
    {
        var handler = new HttpClientHandler
        {
            UseProxy = false
        };

        if (proxyEnable != 1 || string.IsNullOrWhiteSpace(proxyServer))
        {
            return handler;
        }

        var address = SelectProxyAddress(proxyServer);
        if (!address.Contains("://", StringComparison.Ordinal))
        {
            address = "http://" + address;
        }

        if (Uri.TryCreate(address, UriKind.Absolute, out var proxyUri))
        {
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(proxyUri);
        }

        return handler;
    }

    internal static HttpMessageHandler CreateDefaultHandler()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings",
                writable: false);
            var proxyEnable = key?.GetValue("ProxyEnable") switch
            {
                int number => number,
                string text when int.TryParse(text, out var number) => number,
                _ => (int?)null
            };
            return CreateHandler(proxyEnable, key?.GetValue("ProxyServer") as string);
        }
        catch (IOException)
        {
            return CreateHandler(null, null);
        }
        catch (UnauthorizedAccessException)
        {
            return CreateHandler(null, null);
        }
        catch (System.Security.SecurityException)
        {
            return CreateHandler(null, null);
        }
    }

    private static string SelectProxyAddress(string proxyServer)
    {
        foreach (var candidate in proxyServer.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = candidate.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 &&
                parts[0].Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                return parts[1];
            }
        }

        foreach (var candidate in proxyServer.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = candidate.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 &&
                parts[0].Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                return parts[1];
            }
        }

        return proxyServer.Trim();
    }


    public async Task<ConnectivityResult> VerifyAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ConnectivityEndpoint);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new ConnectivityResult(true, response.StatusCode, null);
            }

            return new ConnectivityResult(
                false,
                response.StatusCode,
                $"联网验证失败：服务器返回 HTTP {(int)response.StatusCode}。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new ConnectivityResult(false, null, "联网验证超时，请稍后重试。");
        }
        catch (TimeoutException)
        {
            return new ConnectivityResult(false, null, "联网验证超时，请稍后重试。");
        }
        catch (HttpRequestException)
        {
            return new ConnectivityResult(false, null, "无法建立 HTTPS 连接，请确认代理已关闭或网络可用。");
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
