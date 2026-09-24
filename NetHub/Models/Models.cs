namespace NetHub.Models;

public sealed class AdapterInfo
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Status { get; init; }
    public required string LinkSpeed { get; init; }
    public int IfIndex { get; init; }

    public string Display => $"{Name}  ·  {Status}";
}

public sealed record AdapterDetail(
    string Name,
    string Status,
    string LinkSpeed,
    string? Ipv4,
    int? PrefixLength,
    string? Ipv6,
    bool IsDhcp,
    string? Gateway,
    IReadOnlyList<string> DnsServers);

public sealed record OperationResult(bool Success, string Message);
