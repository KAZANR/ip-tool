namespace NetworkDoctor.Tests;

using System.Text.Json;
using NetworkDoctor.Core;
using NetworkDoctor.Windows;

public class ProxyRepairerTests
{
    [Fact]
    public void BackupCanRoundTripThroughJson()
    {
        var backup = new ProxyBackup
        {
            RegistryValues = new Dictionary<string, ProxyRegistryValueBackup>
            {
                ["ProxyEnable"] = new(ProxyRegistryValueKind.Int32, null, 1),
                ["ProxyServer"] = new(ProxyRegistryValueKind.String, "proxy.example:8080", null)
            },
            EnvironmentValues = new Dictionary<string, string?>
            {
                ["HTTP_PROXY"] = "http://proxy.example:8080",
                ["NO_PROXY"] = null
            }
        };

        var json = JsonSerializer.Serialize(backup);
        var restored = JsonSerializer.Deserialize<ProxyBackup>(json);

        Assert.NotNull(restored);
        Assert.Equal(1, restored.RegistryValues["ProxyEnable"].Int32Value);
        Assert.Equal("proxy.example:8080", restored.RegistryValues["ProxyServer"].StringValue);
        Assert.Equal("http://proxy.example:8080", restored.EnvironmentValues["HTTP_PROXY"]);
        Assert.Null(restored.EnvironmentValues["NO_PROXY"]);
    }

    [Fact]
    public async Task RepairClearsConfiguredValuesAndSkipsWinHttp()
    {
        var store = new FakeProxySettingsStore(CreateConfiguredSettings());
        IProxyRepairer repairer = new WindowsProxyRepairer(store);

        var result = await repairer.RepairAsync();

        Assert.Equal(6, result.Items.Count(item => item.Status == ProxyRepairStatus.Succeeded));
        Assert.Equal(1, result.Items.Count(item => item.Status == ProxyRepairStatus.Skipped));
        Assert.Equal(ProxySource.WinHttp, result.Items.Single(item => item.Status == ProxyRepairStatus.Skipped).Source);
        Assert.Contains("管理员权限", result.Items.Single(item => item.Status == ProxyRepairStatus.Skipped).Message);
        Assert.Empty(store.RegistryValues);
        Assert.Null(store.EnvironmentValues["HTTP_PROXY"]);
        Assert.Null(store.EnvironmentValues["HTTPS_PROXY"]);
        Assert.Null(store.EnvironmentValues["ALL_PROXY"]);
        Assert.Equal("localhost", store.EnvironmentValues["NO_PROXY"]);
        Assert.Equal(1, result.Backup.RegistryValues["ProxyEnable"].Int32Value);
        Assert.Equal("http://http-proxy.example:8080", result.Backup.EnvironmentValues["HTTP_PROXY"]);
    }

    [Fact]
    public async Task RestoreRestoresRegistryAndUserEnvironmentValues()
    {
        var backup = new ProxyBackup
        {
            RegistryValues = new Dictionary<string, ProxyRegistryValueBackup>
            {
                ["ProxyEnable"] = new(ProxyRegistryValueKind.Int32, null, 0),
                ["ProxyServer"] = new(ProxyRegistryValueKind.String, "proxy.example:8080", null)
            },
            EnvironmentValues = new Dictionary<string, string?>
            {
                ["HTTP_PROXY"] = "http://proxy.example:8080",
                ["NO_PROXY"] = "localhost"
            }
        };
        var store = new FakeProxySettingsStore(CreateEmptySettings());
        IProxyRepairer repairer = new WindowsProxyRepairer(store);

        var result = await repairer.RestoreAsync(backup);

        Assert.Equal(3, result.Items.Count(item => item.Status == ProxyRepairStatus.Succeeded));
        Assert.Equal(0, result.Items.Count(item => item.Status == ProxyRepairStatus.Failed));
        Assert.Equal(0, store.RegistryValues["ProxyEnable"].Int32Value);
        Assert.Equal("proxy.example:8080", store.RegistryValues["ProxyServer"].StringValue);
        Assert.Equal("http://proxy.example:8080", store.EnvironmentValues["HTTP_PROXY"]);
        Assert.Equal("localhost", store.EnvironmentValues["NO_PROXY"]);
    }

    [Fact]
    public async Task RepairReportsStoreFailureWithoutStoppingOtherItems()
    {
        var settings = CreateConfiguredSettings();
        var store = new FakeProxySettingsStore(settings) { ThrowOnDelete = true };
        IProxyRepairer repairer = new WindowsProxyRepairer(store);

        var result = await repairer.RepairAsync();

        Assert.Equal(3, result.Items.Count(item => item.Status == ProxyRepairStatus.Failed));
        Assert.Equal(1, result.Items.Count(item => item.Status == ProxyRepairStatus.Skipped));
        Assert.Contains(result.Items, item => item.Status == ProxyRepairStatus.Succeeded &&
            item.Name == "HTTPS_PROXY");
    }

    private static Dictionary<string, ProxyRegistryValueBackup> CreateConfiguredSettings()
    {
        return new Dictionary<string, ProxyRegistryValueBackup>
        {
            ["ProxyEnable"] = new(ProxyRegistryValueKind.Int32, null, 1),
            ["ProxyServer"] = new(ProxyRegistryValueKind.String, "proxy.example:8080", null),
            ["AutoConfigURL"] = new(ProxyRegistryValueKind.String, "http://pac.example/proxy.pac", null)
        };
    }

    private static Dictionary<string, ProxyRegistryValueBackup> CreateEmptySettings()
    {
        return new Dictionary<string, ProxyRegistryValueBackup>
        {
            ["ProxyEnable"] = new(ProxyRegistryValueKind.Missing, null, null),
            ["ProxyServer"] = new(ProxyRegistryValueKind.Missing, null, null),
            ["AutoConfigURL"] = new(ProxyRegistryValueKind.Missing, null, null)
        };
    }

    private sealed class FakeProxySettingsStore : IProxySettingsStore
    {
        public FakeProxySettingsStore(Dictionary<string, ProxyRegistryValueBackup> registryValues)
        {
            RegistryValues = registryValues;
            EnvironmentValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["HTTP_PROXY"] = "http://http-proxy.example:8080",
                ["HTTPS_PROXY"] = "https://https-proxy.example:8443",
                ["ALL_PROXY"] = "socks5://all-proxy.example:1080",
                ["NO_PROXY"] = "localhost"
            };
        }

        public Dictionary<string, ProxyRegistryValueBackup> RegistryValues { get; }
        public Dictionary<string, string?> EnvironmentValues { get; }
        public bool ThrowOnDelete { get; init; }

        public ProxyBackup Read()
        {
            return new ProxyBackup
            {
                RegistryValues = new Dictionary<string, ProxyRegistryValueBackup>(RegistryValues),
                EnvironmentValues = new Dictionary<string, string?>(EnvironmentValues)
            };
        }

        public void DeleteRegistryValue(string name)
        {
            if (ThrowOnDelete)
            {
                throw new IOException("delete failed");
            }

            RegistryValues.Remove(name);
        }

        public void SetRegistryValue(string name, ProxyRegistryValueBackup value)
        {
            RegistryValues[name] = value;
        }

        public void SetUserEnvironmentVariable(string name, string? value)
        {
            EnvironmentValues[name] = value;
        }
    }
}
