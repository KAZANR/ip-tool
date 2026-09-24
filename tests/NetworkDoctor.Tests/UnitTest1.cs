namespace NetworkDoctor.Tests;

using NetworkDoctor.Core;

public class ProxyDiagnosisTests
{
    [Fact]
    public void ActiveEnvironmentProxyCreatesRepairPlan()
    {
        var findings = new[]
        {
            new ProxyFinding(ProxySource.Environment, "User", "HTTP_PROXY", "http://127.0.0.1:7890", "浏览器和命令行可能无法访问网站"),
            new ProxyFinding(ProxySource.WinInet, "System", "ProxyServer", "", "未检测到系统代理"),
        };

        var result = ProxyDiagnosis.CreateRepairPlan(findings);

        Assert.Single(result.FindingsToClear);
        Assert.Equal(ProxySource.Environment, result.FindingsToClear[0].Source);
        Assert.Equal("关闭 1 个遗留代理设置", result.Summary);
    }
}
