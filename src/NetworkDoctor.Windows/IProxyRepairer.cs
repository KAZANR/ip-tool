namespace NetworkDoctor.Windows;

using NetworkDoctor.Core;

public interface IProxyRepairer
{
    Task<ProxyRepairResult> RepairAsync(CancellationToken cancellationToken = default);

    Task<ProxyRepairResult> RestoreAsync(
        ProxyBackup backup,
        CancellationToken cancellationToken = default);
}
