namespace NetworkDoctor.Tests;

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NetworkDoctor.Windows;

public sealed class NetworkManagementServiceTests
{
    [Fact]
    public void ListAdaptersFiltersNoiseAndOrdersActiveEthernetFirst()
    {
        var active = CreateAdapter("Ethernet", OperationalStatus.Up, 1_000_000_000);
        var disconnected = CreateAdapter("Wi-Fi", OperationalStatus.Down, 100_000_000);
        var loopback = CreateAdapter("Loopback", OperationalStatus.Up, 0) with
        {
            NetworkInterfaceType = NetworkInterfaceType.Loopback
        };
        var noise = CreateAdapter("Debug Adapter", OperationalStatus.Up, 1_000_000_000);
        var service = CreateService(active, disconnected, loopback, noise);

        var adapters = service.ListAdapters();

        Assert.Collection(
            adapters,
            adapter =>
            {
                Assert.Equal("Ethernet", adapter.Name);
                Assert.Equal("1 Gbps", adapter.LinkSpeed);
                Assert.Equal(12, adapter.IfIndex);
            },
            adapter =>
            {
                Assert.Equal("Wi-Fi", adapter.Name);
                Assert.Equal("100 Mbps", adapter.LinkSpeed);
            });
    }

    [Fact]
    public void GetDetailResolvesIpv4Ipv6GatewayAndDns()
    {
        var adapter = CreateAdapter("Ethernet", OperationalStatus.Up, 1_000_000_000) with
        {
            Ipv4Address = IPAddress.Parse("192.0.2.10"),
            Ipv4Mask = IPAddress.Parse("255.255.255.0"),
            Ipv6Addresses =
            [
                IPAddress.Parse("fe80::1"),
                IPAddress.Parse("2001:db8::10"),
                IPAddress.Parse("::1")
            ],
            GatewayAddresses =
            [
                IPAddress.Parse("fe80::1"),
                IPAddress.Parse("192.0.2.1")
            ],
            DnsAddresses =
            [
                IPAddress.Parse("1.1.1.1"),
                IPAddress.Parse("2606:4700:4700::1111"),
                IPAddress.Parse("8.8.8.8")
            ],
            IsDhcpEnabled = true
        };
        var service = CreateService(adapter);

        var detail = service.GetDetail("ethernet");

        Assert.NotNull(detail);
        Assert.Equal("192.0.2.10", detail.Ipv4);
        Assert.Equal(24, detail.PrefixLength);
        Assert.Equal("2001:db8::10", detail.Ipv6);
        Assert.True(detail.IsDhcp);
        Assert.Equal("192.0.2.1", detail.Gateway);
        Assert.Equal(["1.1.1.1", "8.8.8.8"], detail.DnsServers);
    }

    [Fact]
    public void GetDetailTreatsUnknownDhcpStateAsStatic()
    {
        var adapter = CreateAdapter("Ethernet", OperationalStatus.Up, 1_000_000_000) with
        {
            Ipv4Address = IPAddress.Parse("192.0.2.10"),
            Ipv4Mask = IPAddress.Parse("255.255.255.0"),
            IsDhcpStateKnown = false
        };
        var service = CreateService(adapter);

        var detail = service.GetDetail(adapter.Description);

        Assert.NotNull(detail);
        Assert.False(detail.IsDhcp);
    }

    [Theory]
    [InlineData("invalid", "255.255.255.0", null, null, null, "IP 地址格式不正确")]
    [InlineData("192.0.2.10", "255.0.255.0", null, null, null, "子网掩码格式不正确")]
    [InlineData("192.0.2.10", "255.255.255.0", "invalid", null, null, "IP 地址格式不正确")]
    [InlineData("192.0.2.10", "255.255.255.0", null, "invalid", null, "IP 地址格式不正确")]
    public void ApplyStaticRejectsInvalidIpv4Configuration(
        string ip,
        string mask,
        string? gateway,
        string? dns1,
        string? dns2,
        string expectedMessage)
    {
        var runner = new FakeNetworkCommandRunner();
        var service = CreateService(commandRunner: runner);

        var result = service.ApplyStatic("Ethernet", ip, mask, gateway, dns1, dns2);

        Assert.False(result.Success);
        Assert.Contains(expectedMessage, result.Message);
        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public void ApplyStaticBuildsStaticAddressAndDnsCommandsWithSeparateArguments()
    {
        var runner = new FakeNetworkCommandRunner();
        var service = CreateService(commandRunner: runner);

        var result = service.ApplyStatic(
            "Ethernet & malicious",
            "192.0.2.10",
            "255.255.255.0",
            "192.0.2.1",
            "1.1.1.1",
            "8.8.8.8");

        Assert.True(result.Success);
        Assert.Equal(3, runner.Invocations.Count);
        Assert.Equal(
        [
            "interface", "ip", "set", "address", "name=Ethernet & malicious",
            "source=static", "addr=192.0.2.10", "mask=255.255.255.0",
            "gateway=192.0.2.1", "gwmetric=1"
        ], runner.Invocations[0]);
        Assert.Equal(
        [
            "interface", "ip", "set", "dns", "name=Ethernet & malicious",
            "source=static", "addr=1.1.1.1", "validate=no"
        ], runner.Invocations[1]);
        Assert.Equal(
        [
            "interface", "ip", "add", "dns", "name=Ethernet & malicious",
            "addr=8.8.8.8", "index=2", "validate=no"
        ], runner.Invocations[2]);
    }

    [Fact]
    public void ApplyStaticOmitsOptionalGatewayAndDnsArguments()
    {
        var runner = new FakeNetworkCommandRunner();
        var service = CreateService(commandRunner: runner);

        var result = service.ApplyStatic("Ethernet", "192.0.2.10", "255.255.255.0", null, null, null);

        Assert.True(result.Success);
        var invocation = Assert.Single(runner.Invocations);
        Assert.Equal(
        [
            "interface", "ip", "set", "address", "name=Ethernet",
            "source=static", "addr=192.0.2.10", "mask=255.255.255.0"
        ], invocation);
    }

    [Fact]
    public void ApplyDhcpBuildsAddressAndDnsCommandsWithSeparateArguments()
    {
        var runner = new FakeNetworkCommandRunner();
        var service = CreateService(commandRunner: runner);

        var result = service.ApplyDhcp("Wi-Fi 2");

        Assert.True(result.Success);
        Assert.Equal(2, runner.Invocations.Count);
        Assert.Equal(
            ["interface", "ip", "set", "address", "name=Wi-Fi 2", "source=dhcp"],
            runner.Invocations[0]);
        Assert.Equal(
            ["interface", "ip", "set", "dns", "name=Wi-Fi 2", "source=dhcp"],
            runner.Invocations[1]);
    }

    [Fact]
    public void NetshStartInfoUsesArgumentListInsteadOfArgumentsString()
    {
        string[] arguments = ["interface", "ip", "set", "address", "name=Ethernet & malicious"];

        var startInfo = NetworkManagementService.CreateNetshStartInfo(arguments);

        Assert.Equal("netsh.exe", startInfo.FileName);
        Assert.True(string.IsNullOrEmpty(startInfo.Arguments));
        Assert.Equal(arguments, startInfo.ArgumentList);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.True(startInfo.CreateNoWindow);
    }

    [Fact]
    public async Task GetPublicIpsReturnsMatchingIpv4AndIpv6Responses()
    {
        var handler = new StubHttpMessageHandler(
            "203.0.113.7",
            "2001:db8::7");
        var service = CreateService(handler: handler);

        var addresses = await service.GetPublicIpsAsync();

        Assert.Equal("203.0.113.7", addresses.Ipv4);
        Assert.Equal("2001:db8::7", addresses.Ipv6);
    }

    private static NetworkManagementService CreateService(
        params NetworkAdapterSnapshot[] adapters)
    {
        return CreateService(new FakeNetworkAdapterProvider(adapters));
    }

    private static NetworkManagementService CreateService(
        INetworkAdapterProvider provider,
        INetworkCommandRunner? commandRunner = null,
        HttpMessageHandler? handler = null)
    {
        return new NetworkManagementService(
            provider,
            commandRunner ?? new FakeNetworkCommandRunner(),
            handler ?? new StubHttpMessageHandler());
    }

    private static NetworkManagementService CreateService(
        INetworkCommandRunner commandRunner)
    {
        return CreateService(new FakeNetworkAdapterProvider(), commandRunner);
    }

    private static NetworkManagementService CreateService(HttpMessageHandler handler)
    {
        return CreateService(new FakeNetworkAdapterProvider(), handler: handler);
    }

    private static NetworkAdapterSnapshot CreateAdapter(
        string name,
        OperationalStatus status,
        long speed)
    {
        return new NetworkAdapterSnapshot
        {
            Name = name,
            Description = $"{name} Adapter",
            NetworkInterfaceType = NetworkInterfaceType.Ethernet,
            OperationalStatus = status,
            Speed = speed,
            IfIndex = 12,
            IsDhcpStateKnown = true
        };
    }

    private sealed class FakeNetworkAdapterProvider : INetworkAdapterProvider
    {
        private readonly IReadOnlyList<NetworkAdapterSnapshot> _adapters;

        public FakeNetworkAdapterProvider(params NetworkAdapterSnapshot[] adapters)
        {
            _adapters = adapters;
        }

        public IReadOnlyList<NetworkAdapterSnapshot> GetAdapters() => _adapters;
    }

    private sealed class FakeNetworkCommandRunner : INetworkCommandRunner
    {
        public List<IReadOnlyList<string>> Invocations { get; } = [];

        public NetworkCommandResult Run(IReadOnlyList<string> arguments)
        {
            Invocations.Add(arguments.ToArray());
            return new(0, string.Empty);
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public StubHttpMessageHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue())
            });
        }
    }
}
