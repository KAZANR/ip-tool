namespace NetworkDoctor.Windows;

public sealed class NetworkAdapterInfo
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Status { get; init; }
    public required string LinkSpeed { get; init; }
    public int IfIndex { get; init; }

    public string Display => $"{Name}  ·  {Status}";
}

public sealed record NetworkAdapterDetail(
    string Name,
    string Status,
    string LinkSpeed,
    string? Ipv4,
    int? PrefixLength,
    string? Ipv6,
    bool IsDhcp,
    string? Gateway,
    IReadOnlyList<string> DnsServers);

public sealed record NetworkOperationResult(bool Success, string Message);

public sealed record PublicIpAddresses(string? Ipv4, string? Ipv6);
