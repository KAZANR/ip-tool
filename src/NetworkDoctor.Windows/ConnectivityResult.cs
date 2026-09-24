namespace NetworkDoctor.Windows;

using System.Net;

public sealed record ConnectivityResult(
    bool IsConnected,
    HttpStatusCode? StatusCode,
    string? ErrorSummary);
