namespace NetworkDoctor.Tests;

using System.Net;
using System.Text;
using NetworkDoctor.Windows;

public sealed class RouterServiceTests
{
    [Fact]
    public void DpapiCredentialStoreDoesNotPersistPlaintextPassword()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        try
        {
            var store = new DpapiCredentialStore(path);

            store.Save("router-password");

            var storedText = File.ReadAllText(path);
            Assert.DoesNotContain("router-password", storedText);
            Assert.Equal("router-password", new DpapiCredentialStore(path).Load());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CreateLoginRequestUsesLuciHashAndKeepsPasswordOutOfUri()
    {
        const string password = "correct horse battery staple";
        const string nonce = "0_device-1_1700000000_1234";

        using var request = MiRouterService.CreateLoginRequest(
            "192.168.31.1",
            password,
            "device-1",
            "0123456789abcdef0123456789abcdef",
            nonce);

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            new Uri("http://192.168.31.1/cgi-bin/luci/api/xqsystem/login"),
            request.RequestUri);
        var body = await request.Content!.ReadAsStringAsync();
        Assert.Contains("username=admin", body);
        Assert.Contains("logtype=2", body);
        Assert.Contains($"nonce={nonce}", body);
        Assert.Contains("password=070f86b962e151533c274a35f484e9cc46da9ae3", body);
        Assert.DoesNotContain(password, body);
        Assert.DoesNotContain(password, request.RequestUri!.ToString());
    }

    [Fact]
    public async Task CreatePppoeRequestUsesStokInRouterPath()
    {
        using var request = MiRouterService.CreatePppoeRequest("192.168.31.1", "abc123");

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            new Uri("http://192.168.31.1/cgi-bin/luci/;stok=abc123/api/xqsystem/information"),
            request.RequestUri);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task CreateRedialRequestPutsPppoeCredentialsOnlyInFormBody()
    {
        using var request = MiRouterService.CreateRedialRequest(
            "192.168.31.1",
            "abc123",
            "user@example.com",
            "p@ss word");

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            new Uri("http://192.168.31.1/cgi-bin/luci/;stok=abc123/api/xqnetwork/set_wan"),
            request.RequestUri);
        var body = await request.Content!.ReadAsStringAsync();
        Assert.Contains("wanType=pppoe", body);
        Assert.Contains("pppoeName=user%40example.com", body);
        Assert.Contains("pppoePwd=p%40ss+word", body);
        Assert.DoesNotContain("p@ss word", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task LoginReadsChallengeAndReturnsToken()
    {
        var handler = new RecordingHandler(
            "<html>deviceId = 'device-1'; key: '0123456789abcdef0123456789abcdef';</html>",
            "{\"code\":0,\"url\":\"http://192.168.31.1/web?stok=abc123\"}");
        using var service = new MiRouterService("192.168.31.1", handler);

        var token = await service.LoginAsync("router-password");

        Assert.Equal("abc123", token);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
    }

    [Fact]
    public async Task GetPppoeCredentialsReadsStoredRouterValues()
    {
        var handler = new RecordingHandler("{\"username\":\"user@example.com\",\"password\":\"router-password\"}");
        using var service = new MiRouterService("192.168.31.1", handler);

        var credentials = await service.GetPppoeCredentialsAsync("abc123");

        Assert.Equal("user@example.com", credentials.User);
        Assert.Equal("router-password", credentials.Password);
    }

    [Fact]
    public async Task RedialPppoeAcceptsSuccessfulRouterResponse()
    {
        var handler = new RecordingHandler("{\"code\":0}");
        using var service = new MiRouterService("192.168.31.1", handler);

        await service.RedialPppoeAsync("abc123", "user@example.com", "router-password");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        var body = request.Body;
        Assert.Contains("pppoePwd=router-password", body!);
    }

    private sealed record RequestSnapshot(
        HttpMethod Method,
        Uri? RequestUri,
        string? Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public RecordingHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public List<RequestSnapshot> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RequestSnapshot(request.Method, request.RequestUri, body));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json")
            };
        }
    }
}
