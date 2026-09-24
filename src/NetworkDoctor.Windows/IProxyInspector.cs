namespace NetworkDoctor.Windows;

using NetworkDoctor.Core;

public interface IProxyInspector
{
    IReadOnlyList<ProxyFinding> Inspect();
}
