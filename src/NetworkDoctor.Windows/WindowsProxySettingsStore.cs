namespace NetworkDoctor.Windows;

using Microsoft.Win32;

internal sealed class WindowsProxySettingsStore : IProxySettingsStore
{
    private const string InternetSettingsPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private static readonly string[] RegistryNames = ["ProxyEnable", "ProxyServer", "AutoConfigURL"];
    private static readonly string[] EnvironmentNames = ["HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY"];

    public ProxyBackup Read()
    {
        var backup = new ProxyBackup();
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: false);

        foreach (var name in RegistryNames)
        {
            backup.RegistryValues[name] = ReadRegistryValue(key, name);
        }

        foreach (var name in EnvironmentNames)
        {
            backup.EnvironmentValues[name] = Environment.GetEnvironmentVariable(
                name,
                EnvironmentVariableTarget.User);
        }

        return backup;
    }

    public void DeleteRegistryValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void SetRegistryValue(string name, ProxyRegistryValueBackup value)
    {
        if (value.ValueKind == ProxyRegistryValueKind.Missing)
        {
            DeleteRegistryValue(name);
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(InternetSettingsPath, writable: true)
            ?? throw new IOException("无法打开 Internet Settings 注册表项");
        switch (value.ValueKind)
        {
            case ProxyRegistryValueKind.String:
                if (value.StringValue is not string text)
                {
                    throw new InvalidOperationException("注册表字符串值缺失");
                }

                key.SetValue(name, text, RegistryValueKind.String);
                break;
            case ProxyRegistryValueKind.Int32:
                if (value.Int32Value is not int number)
                {
                    throw new InvalidOperationException("注册表整数值缺失");
                }

                key.SetValue(name, number, RegistryValueKind.DWord);
                break;
            default:
                throw new InvalidOperationException("不支持的注册表值类型");
        }
    }

    public void SetUserEnvironmentVariable(string name, string? value)
    {
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
    }

    private static ProxyRegistryValueBackup ReadRegistryValue(RegistryKey? key, string name)
    {
        var value = key?.GetValue(name);
        return value switch
        {
            null => new ProxyRegistryValueBackup(ProxyRegistryValueKind.Missing, null, null),
            int number => new ProxyRegistryValueBackup(ProxyRegistryValueKind.Int32, null, number),
            string text => new ProxyRegistryValueBackup(ProxyRegistryValueKind.String, text, null),
            _ => throw new InvalidOperationException($"不支持的注册表值类型：{name}")
        };
    }
}
