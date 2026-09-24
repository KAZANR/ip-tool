namespace NetworkDoctor.Core;

public enum ProxySource
{
    WinInet,
    WinHttp,
    Environment,
    Browser,
    Unknown
}

public sealed record ProxyFinding(
    ProxySource Source,
    string Scope,
    string Name,
    string Address,
    string Impact);

public sealed record ProxyRepairPlan(
    IReadOnlyList<ProxyFinding> FindingsToClear,
    string Summary);

public static class ProxyDiagnosis
{
    public static ProxyRepairPlan CreateRepairPlan(IEnumerable<ProxyFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var activeFindings = findings
            .Where(finding => !string.IsNullOrWhiteSpace(finding.Address))
            .ToArray();

        return new ProxyRepairPlan(
            activeFindings,
            $"关闭 {activeFindings.Length} 个遗留代理设置");
    }
}
