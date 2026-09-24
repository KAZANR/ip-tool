namespace NetworkDoctor.Tests;

using System.Net;
using System.Text.Json;
using NetworkDoctor.Cli;
using NetworkDoctor.Core;
using NetworkDoctor.Windows;

public class CliContractTests
{
    [Fact]
    public void ClassifiesOnlyFixedCommands()
    {
        Assert.Equal(CliCommand.Inspect, CliContract.Classify(["inspect"]).Command);
        Assert.Equal(CliCommand.Repair, CliContract.Classify(["repair"]).Command);
        var restore = CliContract.Classify(["restore", "backup.json"]);
        var adapters = CliContract.Classify(["adapters"]);
        var publicIp = CliContract.Classify(["public-ip"]);
        var staticIp = CliContract.Classify(
            ["ip-static", "Ethernet", "192.0.2.10", "255.255.255.0", "192.0.2.1", "1.1.1.1", "8.8.8.8"]);
        var dhcp = CliContract.Classify(["ip-dhcp", "Ethernet"]);
        var redial = CliContract.Classify(["redial", "192.168.31.1"]);
        var routerSave = CliContract.Classify(["router-save", "router.local"]);
        var verify = CliContract.Classify(["verify"]);

        Assert.Equal(CliCommand.Restore, restore.Command);
        Assert.Equal("backup.json", restore.BackupFile);
        Assert.Equal(CliCommand.Adapters, adapters.Command);
        Assert.Equal(CliCommand.PublicIp, publicIp.Command);
        Assert.Equal(CliCommand.IpStatic, staticIp.Command);
        Assert.Equal("Ethernet", staticIp.Adapter);
        Assert.Equal("192.0.2.10", staticIp.Ip);
        Assert.Equal("255.255.255.0", staticIp.Mask);
        Assert.Equal("192.0.2.1", staticIp.Gateway);
        Assert.Equal("1.1.1.1", staticIp.Dns1);
        Assert.Equal("8.8.8.8", staticIp.Dns2);
        Assert.Equal(CliCommand.IpDhcp, dhcp.Command);
        Assert.Equal("Ethernet", dhcp.Adapter);
        Assert.Equal(CliCommand.Redial, redial.Command);
        Assert.Equal("192.168.31.1", redial.Host);
        Assert.Equal(CliCommand.RouterSave, routerSave.Command);
        Assert.Equal("router.local", routerSave.Host);
        Assert.Equal(CliCommand.Verify, verify.Command);
    }

    [Fact]
    public void RejectsInvalidCommandArguments()
    {
        Assert.Throws<ArgumentException>(() => CliContract.Classify([]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(["unknown"]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(["repair", "extra"]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(["restore"]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(["restore", ""]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(["verify", "extra"]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(["adapters", "extra"]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(["public-ip", "extra"]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(["ip-static", "Ethernet"]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(
            ["ip-static", "", "192.0.2.10", "255.255.255.0", "192.0.2.1", "1.1.1.1", "8.8.8.8"]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(
            ["ip-static", "Ethernet", "", "255.255.255.0", "192.0.2.1", "1.1.1.1", "8.8.8.8"]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(
            ["ip-static", "Ethernet", "192.0.2.10", "255.255.255.0", "192.0.2.1", "1.1.1.1", "8.8.8.8", "extra"]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(["ip-dhcp"]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(["ip-dhcp", ""]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(["ip-dhcp", "Ethernet", "extra"]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(["redial"]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(["redial", "http://192.168.31.1"]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(["redial", "192.168.31.1/path"]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(["router-save"]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(["router-save", "router.local/"]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(["router-save", "999.999.999.999"]));
        Assert.Throws<ArgumentException>(() => CliContract.Classify(["router-save", "router.local", "extra"]));
    }

    [Fact]
    public void InspectJsonContainsRequiredContractFields()
    {
        var findings = new[]
        {
            new ProxyFinding(ProxySource.Environment, "Process", "HTTP_PROXY", "http://proxy.example", "影响网络")
        };

        var json = JsonDocument.Parse(CliContract.SerializeInspect(findings)).RootElement;

        Assert.True(json.GetProperty("ok").GetBoolean());
        Assert.Equal(1, json.GetProperty("findings").GetArrayLength());
        Assert.Equal(90, json.GetProperty("score").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("summary").GetString()));
    }

    [Fact]
    public void OperationJsonContainsRequiredContractFields()
    {
        var result = new ProxyRepairResult(
            new ProxyBackup(),
            [new ProxyRepairItemResult(ProxySource.WinInet, "ProxyServer", ProxyRepairStatus.Succeeded, "已清除")]);

        var repairJson = JsonDocument.Parse(CliContract.SerializeRepair(result, "backup.json")).RootElement;
        var restoreJson = JsonDocument.Parse(CliContract.SerializeRestore(result)).RootElement;
        var verifyJson = JsonDocument.Parse(
            CliContract.SerializeVerify(new ConnectivityResult(true, HttpStatusCode.OK, null))).RootElement;
        var errorJson = JsonDocument.Parse(CliContract.SerializeError("参数无效")).RootElement;

        Assert.Equal("backup.json", repairJson.GetProperty("backup").GetString());
        Assert.Equal(1, repairJson.GetProperty("items").GetArrayLength());
        Assert.True(repairJson.GetProperty("ok").GetBoolean());
        Assert.Equal(1, restoreJson.GetProperty("items").GetArrayLength());
        Assert.True(verifyJson.GetProperty("ok").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(verifyJson.GetProperty("summary").GetString()));
        Assert.False(errorJson.GetProperty("ok").GetBoolean());
        Assert.Equal("参数无效", errorJson.GetProperty("error").GetString());
    }

    [Fact]
    public void NetworkCommandJsonContainsStableFields()
    {
        var adapters = new[] { new NetworkAdapterInfo { Name = "Ethernet", Description = "Adapter", Status = "Up", LinkSpeed = "1 Gbps", IfIndex = 12 } };
        var details = new NetworkAdapterDetail?[]
        {
            new("Ethernet", "Up", "1 Gbps", "192.0.2.10", 24, "2001:db8::10", true, "192.0.2.1", ["1.1.1.1", "8.8.8.8"])
        };
        var adaptersJson = JsonDocument.Parse(CliContract.SerializeAdapters(adapters, details)).RootElement;
        var publicIpJson = JsonDocument.Parse(
            CliContract.SerializePublicIp(new PublicIpAddresses("203.0.113.7", "2001:db8::7"))).RootElement;
        var operationJson = JsonDocument.Parse(
            CliContract.SerializeNetworkOperation(
                CliCommand.IpDhcp,
                "Ethernet",
                new NetworkOperationResult(true, "[Ethernet] 已切换为 DHCP"))).RootElement;
        var redialJson = JsonDocument.Parse(CliContract.SerializeRedial("192.168.31.1")).RootElement;

        Assert.True(adaptersJson.GetProperty("ok").GetBoolean());
        Assert.Equal("adapters", adaptersJson.GetProperty("command").GetString());
        Assert.Equal(1, adaptersJson.GetProperty("adapters").GetArrayLength());
        var adapterJson = adaptersJson.GetProperty("adapters")[0];
        Assert.Equal("192.0.2.10", adapterJson.GetProperty("Ipv4").GetString());
        Assert.Equal(24, adapterJson.GetProperty("PrefixLength").GetInt32());
        Assert.Equal("2001:db8::10", adapterJson.GetProperty("Ipv6").GetString());
        Assert.True(adapterJson.GetProperty("IsDhcp").GetBoolean());
        Assert.Equal("192.0.2.1", adapterJson.GetProperty("Gateway").GetString());
        Assert.Equal(2, adapterJson.GetProperty("DnsServers").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(adaptersJson.GetProperty("summary").GetString()));
        Assert.True(publicIpJson.GetProperty("ok").GetBoolean());
        Assert.Equal("public-ip", publicIpJson.GetProperty("command").GetString());
        Assert.Equal("203.0.113.7", publicIpJson.GetProperty("ipv4").GetString());
        Assert.Equal("2001:db8::7", publicIpJson.GetProperty("ipv6").GetString());
        Assert.True(operationJson.GetProperty("ok").GetBoolean());
        Assert.Equal("ip-dhcp", operationJson.GetProperty("command").GetString());
        Assert.Equal("Ethernet", operationJson.GetProperty("adapter").GetString());
        Assert.Equal("[Ethernet] 已切换为 DHCP", operationJson.GetProperty("message").GetString());
        Assert.Equal("[Ethernet] 已切换为 DHCP", operationJson.GetProperty("summary").GetString());
        Assert.True(redialJson.GetProperty("ok").GetBoolean());
        Assert.Equal("redial", redialJson.GetProperty("command").GetString());
        Assert.Equal("192.168.31.1", redialJson.GetProperty("host").GetString());
    }

    [Fact]
    public async Task NetworkOperationFailureIsReflectedInStableJson()
    {
        var json = JsonDocument.Parse(CliContract.SerializeNetworkOperation(
            CliCommand.IpStatic,
            "Ethernet",
            new NetworkOperationResult(false, "IP 地址格式不正确"))).RootElement;

        Assert.False(json.GetProperty("ok").GetBoolean());
        Assert.Equal("ip-static", json.GetProperty("command").GetString());
        Assert.Equal("Ethernet", json.GetProperty("adapter").GetString());
        Assert.Equal("IP 地址格式不正确", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task RouterSaveOnlySavesStdinPassword()
    {
        const string password = "router-password";
        var router = new RecordingRouterService();
        using var input = new StringReader(password + Environment.NewLine);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await Program.RunAsync(["router-save", "router.local"], input, output, error, _ => router);

        Assert.Equal(0, exitCode);
        Assert.Equal(["save"], router.Calls);
        Assert.Equal(password, router.Password);
        Assert.DoesNotContain(password, output.ToString());
        Assert.DoesNotContain(password, error.ToString());
        Assert.Equal("路由器密码已保存", JsonDocument.Parse(output.ToString()).RootElement.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task RedialUsesStoredPasswordWhenStdinIsEmpty()
    {
        var router = new RecordingRouterService("user@example.com", "pppoe-password")
        {
            StoredPassword = "saved-password"
        };
        using var input = new StringReader(Environment.NewLine);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await Program.RunAsync(["redial", "192.168.31.1"], input, output, error, _ => router);

        Assert.Equal(0, exitCode);
        Assert.Equal(["load", "login", "credentials", "redial"], router.Calls);
        Assert.Equal("saved-password", router.Password);
    }

    [Fact]
    public async Task RedialSavesStdinPasswordBeforeLoginAndRedials()
    {
        const string password = "router-password";
        var router = new RecordingRouterService("user@example.com", "pppoe-password");
        using var input = new StringReader(password + Environment.NewLine);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await Program.RunAsync(["redial", "192.168.31.1"], input, output, error, _ => router);

        Assert.Equal(0, exitCode);
        Assert.Equal(["save", "login", "credentials", "redial"], router.Calls);
        Assert.Equal(password, router.Password);
        Assert.DoesNotContain(password, output.ToString());
        Assert.DoesNotContain(password, error.ToString());
    }

    [Fact]
    public async Task RedialFailureDoesNotExposePassword()
    {
        const string password = "router-password";
        var router = new RecordingRouterService { FailureMessage = $"request rejected for {password}" };
        using var input = new StringReader(password + Environment.NewLine);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await Program.RunAsync(["redial", "192.168.31.1"], input, output, error, _ => router);

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain(password, output.ToString());
        Assert.DoesNotContain(password, error.ToString());
        Assert.Equal("路由器重拨失败", JsonDocument.Parse(error.ToString()).RootElement.GetProperty("error").GetString());
    }

    private sealed class RecordingRouterService : IRouterCommandService
    {
        private readonly string? _user;
        private readonly string? _pppoePassword;

        public RecordingRouterService(string? user = null, string? pppoePassword = null)
        {
            _user = user;
            _pppoePassword = pppoePassword;
        }

        public List<string> Calls { get; } = [];
        public string? Password { get; private set; }
        public string? StoredPassword { get; init; }
        public string? FailureMessage { get; init; }

        public void Dispose()
        {
        }

        public void SavePassword(string password)
        {
            Calls.Add("save");
            Password = password;
        }

        public string? LoadPassword()
        {
            Calls.Add("load");
            Password = StoredPassword;
            return StoredPassword;
        }

        public Task<string> LoginAsync(string password, CancellationToken cancellationToken = default)
        {
            Calls.Add("login");
            FailIfRequested();
            return Task.FromResult("token");
        }

        public Task<(string? User, string? Password)> GetPppoeCredentialsAsync(
            string token,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("credentials");
            FailIfRequested();
            return Task.FromResult((_user, _pppoePassword));
        }

        public Task RedialPppoeAsync(
            string token,
            string user,
            string password,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("redial");
            FailIfRequested();
            return Task.CompletedTask;
        }

        private void FailIfRequested()
        {
            if (FailureMessage is not null)
            {
                throw new InvalidOperationException(FailureMessage);
            }
        }
    }
}
