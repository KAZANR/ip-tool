namespace NetworkDoctor.Windows;

using NetworkDoctor.Core;

public enum ProxyRepairStatus
{
    Succeeded,
    Failed,
    Skipped
}

public sealed record ProxyRepairItemResult(
    ProxySource Source,
    string Name,
    ProxyRepairStatus Status,
    string Message);

public sealed record ProxyRepairResult(
    ProxyBackup Backup,
    IReadOnlyList<ProxyRepairItemResult> Items);

public enum ProxyRegistryValueKind
{
    Missing,
    String,
    Int32
}

public sealed record ProxyRegistryValueBackup(
    ProxyRegistryValueKind ValueKind,
    string? StringValue,
    int? Int32Value);

public sealed class ProxyBackup
{
    public Dictionary<string, ProxyRegistryValueBackup> RegistryValues { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string?> EnvironmentValues { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}
