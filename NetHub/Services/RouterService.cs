using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetHub.Services;

/// <summary>
/// 小米路由器本地 LuCI 客户端：登录、读取 PPPoE、触发重拨。
/// 仅使用路由器已保存的宽带账号，本软件不保存宽带密码。
/// </summary>
public sealed class MiRouterClient
{
    private readonly string _host;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public MiRouterClient(string host = "192.168.31.1") => _host = host;

    public async Task<string> LoginAsync(string password, CancellationToken ct = default)
    {
        var html = await Http.GetStringAsync($"http://{_host}/cgi-bin/luci/web", ct);
        var deviceId = Match(html, @"deviceId\s*=\s*'([^']+)'");
        var key = Match(html, @"key:\s*'([0-9a-f]{32})'");
        if (deviceId is null || key is null)
            throw new InvalidOperationException("无法从路由器获取登录参数（页面结构可能已变化）");

        var nonce = $"0_{deviceId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{Random.Shared.Next(0, 10000)}";
        var pwdHash = Sha1Hex(nonce + Sha1Hex(password + key));

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "admin",
            ["password"] = pwdHash,
            ["logtype"] = "2",
            ["nonce"] = nonce
        });
        var resp = await Http.PostAsync($"http://{_host}/cgi-bin/luci/api/xqsystem/login", content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var code = doc.RootElement.GetProperty("code").GetInt32();
        if (code != 0)
            throw new InvalidOperationException($"路由器登录失败（code={code}，管理密码可能不对）");

        var url = doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
        var m = System.Text.RegularExpressions.Regex.Match(url, "stok=([0-9a-f]+)");
        if (!m.Success) throw new InvalidOperationException("登录成功但未获取到令牌");
        return m.Groups[1].Value;
    }

    public async Task<(string? User, string? Password)> GetPppoeCredentialsAsync(string token, CancellationToken ct = default)
    {
        var json = await Http.GetStringAsync($"http://{_host}/cgi-bin/luci/;stok={token}/api/xqsystem/information", ct);
        var user = Match(json, "\"username\"\\s*:\\s*\"([^\"]+)\"");
        var pwd = Match(json, "\"password\"\\s*:\\s*\"([^\"]+)\"");
        return (user, pwd);
    }

    public async Task RedialPppoeAsync(string token, string user, string password, CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["wanType"] = "pppoe",
            ["pppoeName"] = user,
            ["pppoePwd"] = password
        });
        var resp = await Http.PostAsync(
            $"http://{_host}/cgi-bin/luci/;stok={token}/api/xqnetwork/set_wan", content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var code = doc.RootElement.GetProperty("code").GetInt32();
        if (code != 0)
        {
            var msg = doc.RootElement.TryGetProperty("msg", out var m) ? m.GetString() : "";
            throw new InvalidOperationException($"重拨被拒绝（code={code} msg={msg}）");
        }
    }

    private static string? Match(string text, string pattern)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text, pattern);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string Sha1Hex(string text)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>DPAPI 加密存储路由器管理密码（不依赖 NuGet 包）。</summary>
public static class CredentialStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NetHubRouterCred.xml");

    private static string LegacyPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IPilotRouterCred.xml");

    public static void SavePassword(string plain)
    {
        var bytes = Protect(Encoding.UTF8.GetBytes(plain));
        File.WriteAllText(FilePath, Convert.ToBase64String(bytes));
    }

    public static string? LoadPassword()
    {
        foreach (var path in new[] { FilePath, LegacyPath })
        {
            try
            {
                if (!File.Exists(path)) continue;
                var raw = File.ReadAllText(path).Trim();
                var bytes = Unprotect(Convert.FromBase64String(raw));
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                // try next
            }
        }
        return null;
    }

    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn, string? szDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn, StringBuilder? ppszDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private static byte[] Protect(byte[] plain)
    {
        var inBlob = ToBlob(plain);
        if (!CryptProtectData(ref inBlob, "NetHub", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out var outBlob))
            throw new InvalidOperationException($"CryptProtectData failed: {Marshal.GetLastWin32Error()}");
        try
        {
            var result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
            return result;
        }
        finally
        {
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
        }
    }

    private static byte[] Unprotect(byte[] cipher)
    {
        var inBlob = ToBlob(cipher);
        if (!CryptUnprotectData(ref inBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out var outBlob))
            throw new InvalidOperationException($"CryptUnprotectData failed: {Marshal.GetLastWin32Error()}");
        try
        {
            var result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
            return result;
        }
        finally
        {
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
        }
    }

    private static DATA_BLOB ToBlob(byte[] data)
    {
        var ptr = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, ptr, data.Length);
        return new DATA_BLOB { cbData = data.Length, pbData = ptr };
    }
}
