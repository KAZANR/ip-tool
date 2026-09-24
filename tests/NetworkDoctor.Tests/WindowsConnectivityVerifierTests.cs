namespace NetworkDoctor.Tests;

using System.Net;
using NetworkDoctor.Windows;

public class WindowsConnectivityVerifierTests
{
    [Fact]
    public async Task VerifierRequestsFixedConnectivityEndpoint()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        using var verifier = new WindowsConnectivityVerifier(handler);

        await verifier.VerifyAsync();

        Assert.Equal(
            new Uri("https://www.baidu.com/"),
            handler.RequestUri);
    }

    [Fact]
    public async Task SuccessfulResponseIncludesStatusCode()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        using var verifier = new WindowsConnectivityVerifier(handler);

        var result = await verifier.VerifyAsync();

        Assert.True(result.IsConnected);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Null(result.ErrorSummary);
    }

    [Fact]
    public async Task NonSuccessResponseIncludesStatusCodeAndErrorSummary()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var verifier = new WindowsConnectivityVerifier(handler);

        var result = await verifier.VerifyAsync();

        Assert.False(result.IsConnected);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.Contains("503", result.ErrorSummary);
    }

    [Fact]
    public async Task RequestFailureIncludesErrorSummary()
    {
        var handler = new RecordingHandler(
            new HttpRequestException("simulated connection failure"));
        using var verifier = new WindowsConnectivityVerifier(handler);

        var result = await verifier.VerifyAsync();

        Assert.False(result.IsConnected);
        Assert.Null(result.StatusCode);
        Assert.Contains("无法建立 HTTPS 连接", result.ErrorSummary);
    }

    [Fact]
    public async Task RequestIsCanceledWhenTimeoutExpires()
    {
        var handler = new RecordingHandler(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var verifier = new WindowsConnectivityVerifier(
            handler,
            TimeSpan.FromMilliseconds(20));

        var result = await verifier.VerifyAsync();

        Assert.False(result.IsConnected);
        Assert.Null(result.StatusCode);
        Assert.Contains("超时", result.ErrorSummary);
    }

    [Fact]
    public async Task ResponseBodyIsNotRead()
    {
        var body = new ThrowOnReadStream();
        var content = new StreamContent(body);
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        using var verifier = new WindowsConnectivityVerifier(handler);

        var result = await verifier.VerifyAsync();

        Assert.True(result.IsConnected);
        Assert.False(body.WasRead);
    }

    [Fact]
    public void ConfiguredWinInetProxyIsUsedForVerification()
    {
        using var handler = WindowsConnectivityVerifier.CreateHandler(1, "127.0.0.1:7890");

        Assert.True(handler.UseProxy);
        Assert.NotNull(handler.Proxy);
        Assert.Equal(
            new Uri("http://127.0.0.1:7890/"),
            handler.Proxy.GetProxy(new Uri("https://www.baidu.com/")));
    }

    [Fact]
    public void DisabledWinInetProxyUsesDirectConnection()
    {
        using var handler = WindowsConnectivityVerifier.CreateHandler(0, "127.0.0.1:7890");

        Assert.False(handler.UseProxy);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<CancellationToken, Task<HttpResponseMessage>> _createResponse;

        public RecordingHandler(HttpResponseMessage response)
            : this(_ => Task.FromResult(response))
        {
        }

        public RecordingHandler(Func<CancellationToken, Task<HttpResponseMessage>> createResponse)
        {
            _createResponse = createResponse;
        }

        public RecordingHandler(Exception exception)
        {
            _createResponse = null!;
            Exception = exception;
        }

        public Uri? RequestUri { get; private set; }

        public Exception? Exception { get; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;

            if (Exception is not null)
            {
                throw Exception;
            }

            return _createResponse(cancellationToken);
        }
    }

    private sealed class ThrowOnReadStream : Stream
    {
        public bool WasRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            WasRead = true;
            throw new InvalidOperationException("Response body must not be read");
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            WasRead = true;
            throw new InvalidOperationException("Response body must not be read");
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
