namespace NetworkDoctor.Windows;

using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

public sealed class MiRouterService : IDisposable
{
    private readonly string _host;
    private readonly HttpClient _httpClient;
    private readonly ICredentialStore _credentialStore;

    public MiRouterService(string host = "192.168.31.1")
        : this(
            host,
            new DpapiCredentialStore(),
            new HttpClientHandler { UseProxy = false },
            disposeHandler: true)
    {
    }

    internal MiRouterService(string host, HttpMessageHandler handler)
        : this(host, new DpapiCredentialStore(), handler, disposeHandler: false)
    {
    }

    internal MiRouterService(
        string host,
        HttpMessageHandler handler,
        ICredentialStore credentialStore)
        : this(host, credentialStore, handler, disposeHandler: false)
    {
    }

    private MiRouterService(
        string host,
        ICredentialStore credentialStore,
        HttpMessageHandler handler,
        bool disposeHandler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(credentialStore);

        _host = host.Trim().TrimEnd('/');
        _credentialStore = credentialStore;
        _httpClient = new HttpClient(handler, disposeHandler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public void SavePassword(string password)
    {
        _credentialStore.Save(password);
    }

    public string? LoadPassword()
    {
        return _credentialStore.Load();
    }

    public async Task<string> LoginAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        using var pageRequest = CreateWebRequest(_host);
        using var pageResponse = await _httpClient.SendAsync(
            pageRequest,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        pageResponse.EnsureSuccessStatusCode();
        var page = await pageResponse.Content.ReadAsStringAsync(cancellationToken);
        var deviceId = Match(page, @"deviceId\s*=\s*'([^']+)'");
        var key = Match(page, @"key:\s*'([0-9a-f]{32})'");
        if (deviceId is null || key is null)
        {
            throw new InvalidOperationException("无法从路由器获取登录参数（页面结构可能已变化）");
        }

        var nonce = $"0_{deviceId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{Random.Shared.Next(0, 10000)}";
        using var loginRequest = CreateLoginRequest(_host, password, deviceId, key, nonce);
        using var loginResponse = await _httpClient.SendAsync(
            loginRequest,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        loginResponse.EnsureSuccessStatusCode();
        var body = await loginResponse.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        var code = document.RootElement.GetProperty("code").GetInt32();
        if (code != 0)
        {
            throw new InvalidOperationException($"路由器登录失败（code={code}）");
        }

        var url = document.RootElement.TryGetProperty("url", out var urlElement)
            ? urlElement.GetString() ?? string.Empty
            : string.Empty;
        var token = Match(url, @"stok=([0-9a-f]+)");
        if (token is null)
        {
            throw new InvalidOperationException("登录成功但未获取到令牌");
        }

        return token;
    }

    public async Task<(string? User, string? Password)> GetPppoeCredentialsAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        using var request = CreatePppoeRequest(_host, token);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        return (FindString(document.RootElement, "username"), FindString(document.RootElement, "password"));
    }

    public async Task RedialPppoeAsync(
        string token,
        string user,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        using var request = CreateRedialRequest(_host, token, user, password);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        var code = document.RootElement.GetProperty("code").GetInt32();
        if (code != 0)
        {
            throw new InvalidOperationException($"重拨被拒绝（code={code}）");
        }
    }

    internal static HttpRequestMessage CreateLoginRequest(
        string host,
        string password,
        string deviceId,
        string key,
        string nonce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        var passwordHash = Sha1Hex(nonce + Sha1Hex(password + key));
        return new HttpRequestMessage(
            HttpMethod.Post,
            CreateUri(host, "/cgi-bin/luci/api/xqsystem/login"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = "admin",
                ["password"] = passwordHash,
                ["logtype"] = "2",
                ["nonce"] = nonce
            })
        };
    }

    internal static HttpRequestMessage CreatePppoeRequest(string host, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new HttpRequestMessage(
            HttpMethod.Get,
            CreateUri(host, $"/cgi-bin/luci/;stok={token}/api/xqsystem/information"));
    }

    internal static HttpRequestMessage CreateRedialRequest(
        string host,
        string token,
        string user,
        string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return new HttpRequestMessage(
            HttpMethod.Post,
            CreateUri(host, $"/cgi-bin/luci/;stok={token}/api/xqnetwork/set_wan"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["wanType"] = "pppoe",
                ["pppoeName"] = user,
                ["pppoePwd"] = password
            })
        };
    }

    private static HttpRequestMessage CreateWebRequest(string host)
    {
        return new HttpRequestMessage(HttpMethod.Get, CreateUri(host, "/cgi-bin/luci/web"));
    }

    private static Uri CreateUri(string host, string path)
    {
        return new Uri($"http://{host.Trim().TrimEnd('/')}{path}");
    }

    private static string? Match(string text, string pattern)
    {
        var match = Regex.Match(text, pattern);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? FindString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(propertyName) && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                var nested = FindString(property.Value, propertyName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindString(item, propertyName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string Sha1Hex(string text)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
