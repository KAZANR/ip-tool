namespace NetworkDoctor.Windows;

using NetworkDoctor.Core;

internal interface IProxySettingsStore
{
    ProxyBackup Read();

    void DeleteRegistryValue(string name);

    void SetRegistryValue(string name, ProxyRegistryValueBackup value);

    void SetUserEnvironmentVariable(string name, string? value);
}

public sealed class WindowsProxyRepairer : IProxyRepairer
{
    private static readonly string[] RegistryNames = ["ProxyEnable", "ProxyServer", "AutoConfigURL"];
    private static readonly string[] EnvironmentNames = ["HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY"];
    private readonly IProxySettingsStore _store;

    public WindowsProxyRepairer()
        : this(new WindowsProxySettingsStore())
    {
    }

    internal WindowsProxyRepairer(IProxySettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<ProxyRepairResult> RepairAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var backup = _store.Read();
        var items = new List<ProxyRepairItemResult>();

        foreach (var name in RegistryNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddRegistryRepairItem(items, name, backup);
        }

        foreach (var name in EnvironmentNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddEnvironmentRepairItem(items, name, backup);
        }

        AddWinHttpResult(items);
        return Task.FromResult(new ProxyRepairResult(backup, items));
    }

    public Task<ProxyRepairResult> RestoreAsync(
        ProxyBackup backup,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(backup);
        cancellationToken.ThrowIfCancellationRequested();
        var items = new List<ProxyRepairItemResult>();

        foreach (var name in RegistryNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (backup.RegistryValues.TryGetValue(name, out var value))
            {
                AddRegistryRestoreItem(items, name, value);
            }
        }

        foreach (var name in EnvironmentNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (backup.EnvironmentValues.TryGetValue(name, out var value))
            {
                AddEnvironmentRestoreItem(items, name, value);
            }
        }

        AddWinHttpResult(items);
        return Task.FromResult(new ProxyRepairResult(backup, items));
    }

    private void AddRegistryRepairItem(
        ICollection<ProxyRepairItemResult> items,
        string name,
        ProxyBackup backup)
    {
        if (!backup.RegistryValues.TryGetValue(name, out var value) || value.ValueKind == ProxyRegistryValueKind.Missing)
        {
            items.Add(new ProxyRepairItemResult(
                ProxySource.WinInet,
                name,
                ProxyRepairStatus.Skipped,
                "当前用户没有此注册表值，无需修改"));
            return;
        }

        try
        {
            _store.DeleteRegistryValue(name);
            items.Add(new ProxyRepairItemResult(
                ProxySource.WinInet,
                name,
                ProxyRepairStatus.Succeeded,
                "已清除当前用户 Internet Settings 代理值"));
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            items.Add(new ProxyRepairItemResult(
                ProxySource.WinInet,
                name,
                ProxyRepairStatus.Failed,
                exception.Message));
        }
    }

    private void AddEnvironmentRepairItem(
        ICollection<ProxyRepairItemResult> items,
        string name,
        ProxyBackup backup)
    {
        if (!backup.EnvironmentValues.TryGetValue(name, out var value) || value is null)
        {
            items.Add(new ProxyRepairItemResult(
                ProxySource.Environment,
                name,
                ProxyRepairStatus.Skipped,
                "当前用户没有此环境变量，无需修改"));
            return;
        }

        try
        {
            _store.SetUserEnvironmentVariable(name, null);
            items.Add(new ProxyRepairItemResult(
                ProxySource.Environment,
                name,
                ProxyRepairStatus.Succeeded,
                "已清除当前用户环境变量"));
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            items.Add(new ProxyRepairItemResult(
                ProxySource.Environment,
                name,
                ProxyRepairStatus.Failed,
                exception.Message));
        }
    }

    private void AddRegistryRestoreItem(
        ICollection<ProxyRepairItemResult> items,
        string name,
        ProxyRegistryValueBackup value)
    {
        try
        {
            _store.SetRegistryValue(name, value);
            items.Add(new ProxyRepairItemResult(
                ProxySource.WinInet,
                name,
                ProxyRepairStatus.Succeeded,
                "已恢复当前用户 Internet Settings 代理值"));
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            items.Add(new ProxyRepairItemResult(
                ProxySource.WinInet,
                name,
                ProxyRepairStatus.Failed,
                exception.Message));
        }
    }

    private void AddEnvironmentRestoreItem(
        ICollection<ProxyRepairItemResult> items,
        string name,
        string? value)
    {
        try
        {
            _store.SetUserEnvironmentVariable(name, value);
            items.Add(new ProxyRepairItemResult(
                ProxySource.Environment,
                name,
                ProxyRepairStatus.Succeeded,
                value is null ? "已恢复为未设置" : "已恢复当前用户环境变量"));
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            items.Add(new ProxyRepairItemResult(
                ProxySource.Environment,
                name,
                ProxyRepairStatus.Failed,
                exception.Message));
        }
    }

    private static void AddWinHttpResult(ICollection<ProxyRepairItemResult> items)
    {
        items.Add(new ProxyRepairItemResult(
            ProxySource.WinHttp,
            "WinHTTP",
            ProxyRepairStatus.Skipped,
            "需要管理员权限，暂不支持自动修复"));
    }

    private static bool IsExpectedFailure(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;
    }
}
