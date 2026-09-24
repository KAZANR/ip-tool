namespace NetworkDoctor.Tests;

using NetworkDoctor.Core;
using NetworkDoctor.Windows;

public class WindowsProxyInspectorTests
{
    [Fact]
    public void EnabledWinInetSettingsCreateFindings()
    {
        var findings = WindowsProxyInspector.ParseWinInetSettings(
            1,
            "proxy.example:8080",
            "http://pac.example/proxy.pac");

        Assert.Collection(
            findings,
            finding =>
            {
                Assert.Equal(ProxySource.WinInet, finding.Source);
                Assert.Equal("ProxyServer", finding.Name);
                Assert.Equal("proxy.example:8080", finding.Address);
            },
            finding =>
            {
                Assert.Equal(ProxySource.WinInet, finding.Source);
                Assert.Equal("AutoConfigURL", finding.Name);
                Assert.Equal("http://pac.example/proxy.pac", finding.Address);
            });
    }

    [Fact]
    public void DisabledOrEmptyWinInetSettingsDoNotCreateFindings()
    {
        var disabled = WindowsProxyInspector.ParseWinInetSettings(0, "proxy.example:8080", "   ");
        var empty = WindowsProxyInspector.ParseWinInetSettings(null, null, string.Empty);

        Assert.Empty(disabled);
        Assert.Empty(empty);
    }

    [Theory]
    [InlineData("    WINHTTP_PROXY_NAME: proxy.example:8080\r\n    WINHTTP_BYPASS_LIST: <local>")]
    [InlineData("    代理服务器名称: proxy.example:8080\r\n    代理绕过: <本地>")]
    public void ConfiguredWinHttpProxyIsParsed(string output)
    {
        var finding = Assert.Single(WindowsProxyInspector.ParseWinHttpOutput(output));

        Assert.Equal(ProxySource.WinHttp, finding.Source);
        Assert.Equal("WINHTTP_PROXY_NAME", finding.Name);
        Assert.Equal("proxy.example:8080", finding.Address);
    }

    [Fact]
    public void WinHttpDirectAccessDoesNotCreateFinding()
    {
        var output = "当前的 WinHTTP 代理服务器设置:\r\n\r\n    直接访问(没有代理服务器)。";

        var findings = WindowsProxyInspector.ParseWinHttpOutput(output);

        Assert.Empty(findings);
    }

    [Fact]
    public void NonEmptyProxyEnvironmentVariablesCreateFindings()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["HTTP_PROXY"] = "http://http-proxy.example:8080",
            ["HTTPS_PROXY"] = "https://https-proxy.example:8443",
            ["ALL_PROXY"] = "socks5://all-proxy.example:1080",
            ["NO_PROXY"] = "localhost,.example.com"
        };

        var findings = WindowsProxyInspector.ParseProxyEnvironment(name => values[name]);

        Assert.Collection(
            findings,
            finding => AssertFinding(finding, "HTTP_PROXY", "http://http-proxy.example:8080"),
            finding => AssertFinding(finding, "HTTPS_PROXY", "https://https-proxy.example:8443"),
            finding => AssertFinding(finding, "ALL_PROXY", "socks5://all-proxy.example:1080"));
    }

    [Fact]
    public void EmptyProxyEnvironmentVariablesDoNotCreateFindings()
    {
        var findings = WindowsProxyInspector.ParseProxyEnvironment(_ => null);

        Assert.Empty(findings);
    }

    [Fact]
    public void InspectorReturnsOnlyActiveProxyFindings()
    {
        IProxyInspector inspector = new WindowsProxyInspector();

        var findings = inspector.Inspect();

        Assert.All(findings, finding => Assert.False(string.IsNullOrWhiteSpace(finding.Address)));
    }

    [Fact]
    public void NoProxyIsNotReportedAsAnActiveProxy()
    {
        var findings = WindowsProxyInspector.ParseProxyEnvironment(name => name == "NO_PROXY" ? "localhost" : null);

        Assert.Empty(findings);
    }

    private static void AssertFinding(ProxyFinding finding, string name, string address)
    {
        Assert.Equal(ProxySource.Environment, finding.Source);
        Assert.Equal(name, finding.Name);
        Assert.Equal(address, finding.Address);
    }
}
