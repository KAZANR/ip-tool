using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using NetworkDoctor.Core;
using NetworkDoctor.Windows;

namespace NetworkDoctor.App;

public partial class MainWindow : Window
{
    private readonly WindowsProxyInspector _inspector = new();
    private readonly WindowsProxyRepairer _repairer = new();
    private readonly WindowsConnectivityVerifier _connectivityVerifier = new();
    private readonly ObservableCollection<FindingItem> _findings = [];
    private ProxyBackup? _lastBackup;

    public MainWindow()
    {
        InitializeComponent();
        FindingsList.ItemsSource = _findings;
        Loaded += async (_, _) => await CheckProxyAsync();
    }

    private async void StartCheckButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckProxyAsync();
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckProxyAsync();
    }

    private async void RepairButton_Click(object sender, RoutedEventArgs e)
    {
        if (_findings.Count == 0)
        {
            MessageBox.Show(this, "当前没有需要处理的代理设置。", "Network Doctor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            "将关闭检测到的 Windows 系统代理和当前用户代理环境变量。\n\n原设置会先保存，之后可以点击“撤销”恢复。\n\n是否继续？",
            "确认关闭代理",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var result = await Task.Run(() => _repairer.RepairAsync());
            _lastBackup = result.Backup;
            SaveBackup(result.Backup);
            var connectivity = await Task.Run(() => _connectivityVerifier.VerifyAsync());
            await CheckProxyAsync();
            StatusText.Text = $"{FormatRepairResult(result)}；联网验证：{FormatConnectivity(connectivity)}";
            RestoreButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"修复失败：{exception.Message}";
            ScoreCaption.Text = "修复失败";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastBackup is null)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            "将恢复上次修复前保存的代理设置。是否继续？",
            "确认撤销",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var result = await Task.Run(() => _repairer.RestoreAsync(_lastBackup));
            await CheckProxyAsync();
            StatusText.Text = FormatRepairResult(result);
            RestoreButton.IsEnabled = false;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"撤销失败：{exception.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CheckProxyAsync()
    {
        SetBusy(true);
        try
        {
            var findings = await Task.Run(_inspector.Inspect);
            _findings.Clear();
            foreach (var finding in findings)
            {
                _findings.Add(new FindingItem(finding));
            }

            FindingsList.Items.Refresh();
            FindingsSummary.Text = findings.Count == 0 ? "没有发现代理" : $"发现 {findings.Count} 项代理设置";
            ScoreText.Text = findings.Count == 0 ? "100" : findings.Count == 1 ? "60" : "25";
            ScoreCaption.Text = findings.Count == 0 ? "网络配置正常" : "建议关闭遗留代理";
            StatusText.Text = findings.Count == 0
                ? "没有检测到活动的系统代理或代理环境变量"
                : "代理可能来自系统设置、WinHTTP 或环境变量";
            ScoreText.Foreground = findings.Count == 0
                ? (System.Windows.Media.Brush)FindResource("SuccessBrush")
                : (System.Windows.Media.Brush)FindResource("WarningBrush");
            RepairButton.IsEnabled = findings.Count > 0;
        }
        catch (Exception exception)
        {
            FindingsSummary.Text = "检测失败";
            StatusText.Text = $"无法完成检测：{exception.Message}";
            ScoreText.Text = "—";
            ScoreCaption.Text = "检测失败";
            RepairButton.IsEnabled = false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy)
    {
        CheckProgress.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        CheckProgress.IsIndeterminate = isBusy;
        StartCheckButton.IsEnabled = !isBusy;
        CheckButton.IsEnabled = !isBusy;
        RepairButton.IsEnabled = !isBusy && _findings.Count > 0;
        RestoreButton.IsEnabled = !isBusy && _lastBackup is not null;
    }

    private void SaveBackup(ProxyBackup backup)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetworkDoctor");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "proxy-backup.json");
        File.WriteAllText(path, JsonSerializer.Serialize(backup));
    }

    private static string FormatRepairResult(ProxyRepairResult result)
    {
        var succeeded = result.Items.Count(item => item.Status == ProxyRepairStatus.Succeeded);
        var failed = result.Items.Count(item => item.Status == ProxyRepairStatus.Failed);
        var skipped = result.Items.Count(item => item.Status == ProxyRepairStatus.Skipped);
        return $"处理完成：成功 {succeeded} 项，失败 {failed} 项，跳过 {skipped} 项";
    }

    private static string FormatConnectivity(ConnectivityResult result)
    {
        return result.IsConnected
            ? "已恢复网站访问"
            : $"仍无法访问网站（{result.ErrorSummary ?? "未知错误"}）";
    }

    private sealed record FindingItem(
        string Source,
        string Name,
        string Address,
        string Impact)
    {
        public FindingItem(ProxyFinding finding)
            : this(GetSourceName(finding.Source), finding.Name, finding.Address, finding.Impact)
        {
        }

        private static string GetSourceName(ProxySource source)
        {
            return source switch
            {
                ProxySource.WinInet => "Windows 系统代理",
                ProxySource.WinHttp => "WinHTTP",
                ProxySource.Environment => "环境变量",
                ProxySource.Browser => "浏览器",
                _ => "未知来源"
            };
        }
    }
}
