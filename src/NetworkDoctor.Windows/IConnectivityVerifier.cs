namespace NetworkDoctor.Windows;

public interface IConnectivityVerifier
{
    Task<ConnectivityResult> VerifyAsync(CancellationToken cancellationToken = default);
}
