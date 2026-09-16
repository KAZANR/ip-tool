using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NetHub.Models;
using NetHub.Services;

namespace NetHub;

public partial class MainWindow : Window
{
    // Win11: DWMWA_WINDOW_CORNER_PREFERENCE = 33
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SourceInitialized += (_, _) => ApplyNativeRoundedCorners();
    }

    private void ApplyNativeRoundedCorners()
    {
        // 交给系统圆角，避免自绘圆角 + 方形窗体背景在四角漏出「阴影」
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var pref = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (NetworkService.IsAdministrator())
        {
            TxtAdminBadge.Text = "管理员";
            TxtAdminBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x97, 0xB8));
            AdminDot.Fill = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
        }
        else
        {
            TxtAdminBadge.Text = "普通权限";
            TxtAdminBadge.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
            AdminDot.Fill = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
            AppendLog("未以管理员运行，修改内网 IP 可能失败", "warn");
        }

        var stored = CredentialStore.LoadPassword();
        if (!string.IsNullOrEmpty(stored))
        {
            SetRouterPassword(stored);
            ChkRemember.IsChecked = true;
            AppendLog("已加载本机加密的路由器密码", "ok");
        }

        _isStaticMode = true;
        UpdateModeUi();
        RefreshAdapters();
        TxtSysAdmin.Text = NetworkService.IsAdministrator() ? "管理员" : "普通用户";
        TxtSysCred.Text = CredentialStore.LoadPassword() is not null ? "已保存" : "未保存";
        AppendLog("NetHub v1.6 已就绪", "ok");
        _ = DetectPublicIpAsync(initial: true);
    }

    private async Task DetectPublicIpAsync(bool initial = false)
    {
        if (BtnDetect.IsEnabled is false) return;
        SetBusy(true);
        TxtPublic.Text = "…";
        TxtPublicV6.Text = "…";
        TxtPublicSub.Text = initial ? "启动自动检测 IPv4 / IPv6…" : "检测中…";
        try
        {
            var (v4, v6) = await NetworkService.GetPublicIpsAsync();
            TxtPubTime.Text = DateTime.Now.ToString("HH:mm:ss");

            if (v4 is not null)
            {
                TxtPublic.Text = v4;
                AppendLog($"公网 IPv4: {v4}", "ok");
            }
            else
            {
                TxtPublic.Text = "—";
                AppendLog("公网 IPv4 检测失败", "warn");
            }

            if (v6 is not null)
            {
                TxtPublicV6.Text = v6;
                AppendLog($"公网 IPv6: {v6}", "ok");
            }
            else
            {
                TxtPublicV6.Text = "不可用";
                AppendLog("公网 IPv6 不可用（网络/线路未启用）", "warn");
            }

            TxtPublicSub.Text = (v4, v6) switch
            {
                (not null, not null) => "IPv4 + IPv6 双栈就绪",
                (not null, null) => "仅 IPv4 可用",
                (null, not null) => "仅 IPv6 可用",
                _ => "检测失败，可点刷新重试"
            };
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void AppendLog(string text, string level = "info")
    {
        var tag = level switch
        {
            "ok" => "✓",
            "err" => "×",
            "warn" => "!",
            _ => "·"
        };
        TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {tag}  {text}\r\n");
        TxtLog.ScrollToEnd();
        TxtStatus.Text = text;
    }

    private void SetBusy(bool busy)
    {
        foreach (var b in new System.Windows.Controls.Control[] { BtnRefresh, BtnStatic, BtnDhcp, BtnDetect, BtnRedial, BtnFillCurrent, BtnSegStatic, BtnSegDhcp })
            b.IsEnabled = !busy;
        TxtStatus.Text = busy ? "处理中…" : "就绪";
    }

    private bool _isStaticMode = true;

    private void UpdateModeUi()
    {
        var isStatic = _isStaticMode;
        TxtMode.Text = isStatic ? " 静态" : " DHCP";
        TxtMode.Foreground = new SolidColorBrush(isStatic
            ? Color.FromRgb(0x3B, 0x9B, 0xFF)
            : Color.FromRgb(0xF5, 0xA6, 0x23));
        TxtModeHint.Text = isStatic
            ? "填写后应用。网关 / DNS 留空表示保持原样。"
            : "DHCP 模式下系统自动获取地址。可点「切 DHCP」应用。";

        BtnSegStatic.Style = (System.Windows.Style)FindResource(isStatic ? "SegOn" : "Seg");
        BtnSegDhcp.Style = (System.Windows.Style)FindResource(isStatic ? "Seg" : "SegOn");

        foreach (var t in new[] { TxtIP, TxtMask, TxtGw, TxtDns1, TxtDns2 })
        {
            t.IsEnabled = isStatic;
            t.Opacity = isStatic ? 1.0 : 0.5;
        }
        BtnStatic.IsEnabled = isStatic;
        BtnFillCurrent.IsEnabled = isStatic;
    }

    private void BtnSegStatic_Click(object sender, RoutedEventArgs e)
    {
        _isStaticMode = true;
        UpdateModeUi();
    }

    private void BtnSegDhcp_Click(object sender, RoutedEventArgs e)
    {
        _isStaticMode = false;
        UpdateModeUi();
    }

    private bool _showPassword;

    private void TglShowPwd_Click(object sender, RoutedEventArgs e)
    {
        // 先把两边内容对齐，再切换
        var current = _showPassword ? PwdRouterPlain.Text : PwdRouter.Password;
        _showPassword = !_showPassword;

        if (_showPassword)
        {
            PwdRouterPlain.Text = current ?? "";
            PwdRouter.Visibility = Visibility.Collapsed;
            PwdRouterPlain.Visibility = Visibility.Visible;
            PwdRouterPlain.Focus();
            PwdRouterPlain.CaretIndex = PwdRouterPlain.Text.Length;
            TglShowPwd.Content = "隐藏密码";
        }
        else
        {
            PwdRouter.Password = current ?? "";
            PwdRouterPlain.Visibility = Visibility.Collapsed;
            PwdRouter.Visibility = Visibility.Visible;
            PwdRouter.Focus();
            TglShowPwd.Content = "显示密码";
        }
    }

    private string GetRouterPassword()
    {
        // 以当前可见控件为准
        return _showPassword ? PwdRouterPlain.Text : PwdRouter.Password;
    }

    private void SetRouterPassword(string pwd)
    {
        _showPassword = false;
        PwdRouter.Visibility = Visibility.Visible;
        PwdRouterPlain.Visibility = Visibility.Collapsed;
        TglShowPwd.Content = "显示密码";
        PwdRouter.Password = pwd ?? "";
        PwdRouterPlain.Text = pwd ?? "";
    }

    private void RefreshAdapters()
    {
        var selected = (CmbAdapter.SelectedItem as AdapterInfo)?.Name;
        IReadOnlyList<AdapterInfo> items;
        try
        {
            items = NetworkService.ListAdapters();
        }
        catch (Exception ex)
        {
            AppendLog($"枚举网卡失败: {ex.Message}", "err");
            return;
        }

        CmbAdapter.SelectionChanged -= CmbAdapter_OnSelectionChanged;
        try
        {
            CmbAdapter.ItemsSource = items;

            var idx = -1;
            if (selected is not null)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    if (items[i].Name == selected) { idx = i; break; }
                }
            }

            if (idx < 0)
            {
                // 优先：已连接且能读到 IPv4 的网卡
                for (var i = 0; i < items.Count; i++)
                {
                    if (!items[i].Status.Contains("Up", StringComparison.OrdinalIgnoreCase)) continue;
                    var d = NetworkService.GetDetail(items[i].Name);
                    if (d?.Ipv4 is not null) { idx = i; break; }
                }
            }
            if (idx < 0)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    if (items[i].Status.Contains("Up", StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
                }
            }
            if (idx < 0 && items.Count > 0) idx = 0;

            if (idx >= 0)
            {
                CmbAdapter.SelectedIndex = idx;
                UpdateAdapterInfo();
            }
        }
        finally
        {
            CmbAdapter.SelectionChanged += CmbAdapter_OnSelectionChanged;
        }
    }

    private string? SelectedName => (CmbAdapter.SelectedItem as AdapterInfo)?.Name;

    private void UpdateAdapterInfo()
    {
        var name = SelectedName;
        if (name is null) return;
        AdapterDetail? d;
        try
        {
            d = NetworkService.GetDetail(name);
        }
        catch (Exception ex)
        {
            TxtAdpStatus.Text = "读取失败";
            TxtAdpStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            AppendLog($"读取网卡信息失败: {ex.Message}", "err");
            return;
        }
        if (d is null)
        {
            TxtAdpStatus.Text = "读取失败";
            return;
        }

        TxtAdpStatus.Text = d.Status;
        TxtAdpStatus.Foreground = d.Status.Contains("Up", StringComparison.OrdinalIgnoreCase)
            ? new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A))
            : new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
        TxtAdpSpeed.Text = d.LinkSpeed;
        TxtAdpIp.Text = d.Ipv4 is null ? "未获取" : $"{d.Ipv4}/{d.PrefixLength}";
        TxtAdpAssign.Text = d.IsDhcp ? "DHCP 自动获取" : "静态指定";
        _isStaticMode = !d.IsDhcp;
        UpdateModeUi();
        TxtAdpGw.Text = d.Gateway ?? "无";
        TxtAdpDns.Text = d.DnsServers.Count > 0 ? string.Join("  ·  ", d.DnsServers) : "无";
        TxtSysAdapter.Text = d.Name;

        // 表单：无当前 IP 时给默认掩码，避免空白
        if (string.IsNullOrWhiteSpace(TxtMask.Text))
            TxtMask.Text = "255.255.255.0";
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        try
        {
            DragMove();
        }
        catch
        {
            // 忽略快速点击导致的异常
        }
    }

    private void BtnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void CmbAdapter_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => UpdateAdapterInfo();

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (BtnRefresh.IsEnabled is false) return;
        SetBusy(true);
        try
        {
            RefreshAdapters();
            AppendLog("已刷新网卡列表", "ok");
            await Task.Delay(50);
        }
        finally { SetBusy(false); }
    }

    private void BtnFillCurrent_Click(object sender, RoutedEventArgs e)
    {
        var name = SelectedName;
        if (name is null) { AppendLog("请先选择网卡", "warn"); return; }
        var d = NetworkService.GetDetail(name);
        if (d?.Ipv4 is null) { AppendLog("该网卡当前没有 IPv4 地址", "warn"); return; }

        TxtIP.Text = d.Ipv4;
        if (d.PrefixLength is int p) TxtMask.Text = NetworkService.PrefixToMask(p);
        TxtGw.Text = d.Gateway ?? string.Empty;
        AppendLog($"已填入 {d.Ipv4}/{d.PrefixLength}", "ok");
    }

    private async void BtnStatic_Click(object sender, RoutedEventArgs e)
    {
        var name = SelectedName;
        if (name is null)
        {
            MessageBox.Show(this, "请先选择一个网络适配器。", "NetHub");
            return;
        }

        var ip = TxtIP.Text.Trim();
        var mask = TxtMask.Text.Trim();
        var gw = TxtGw.Text.Trim();
        var dns1 = TxtDns1.Text.Trim();
        var dns2 = TxtDns2.Text.Trim();

        var confirm = $"即将把网卡 [{name}] 设置为:\nIP: {ip}\n掩码: {mask}";
        if (!string.IsNullOrWhiteSpace(gw)) confirm += $"\n网关: {gw}";
        if (!string.IsNullOrWhiteSpace(dns1)) confirm += $"\nDNS: {dns1}" + (string.IsNullOrWhiteSpace(dns2) ? "" : $" / {dns2}");
        confirm += "\n\n确认应用？远程桌面下改错网关可能导致断连。";
        if (MessageBox.Show(this, confirm, "确认修改", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        SetBusy(true);
        try
        {
            var result = await Task.Run(() => NetworkService.ApplyStatic(name, ip, mask,
                string.IsNullOrWhiteSpace(gw) ? null : gw,
                string.IsNullOrWhiteSpace(dns1) ? null : dns1,
                string.IsNullOrWhiteSpace(dns2) ? null : dns2));
            AppendLog(result.Message, result.Success ? "ok" : "err");
            if (!result.Success)
            {
                MessageBox.Show(this, result.Message, "错误");
                return;
            }
            UpdateAdapterInfo();
        }
        finally { SetBusy(false); }
    }

    private async void BtnDhcp_Click(object sender, RoutedEventArgs e)
    {
        var name = SelectedName;
        if (name is null)
        {
            MessageBox.Show(this, "请先选择一个网络适配器。", "NetHub");
            return;
        }
        if (MessageBox.Show(this, $"即将把网卡 [{name}] 切回 DHCP 自动获取，确认？", "确认修改",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        SetBusy(true);
        try
        {
            var result = await Task.Run(() => NetworkService.ApplyDhcp(name));
            AppendLog(result.Message, result.Success ? "ok" : "err");
            if (!result.Success)
            {
                MessageBox.Show(this, result.Message, "错误");
                return;
            }
            UpdateAdapterInfo();
        }
        finally { SetBusy(false); }
    }

    private async void BtnDetect_Click(object sender, RoutedEventArgs e)
        => await DetectPublicIpAsync(initial: false);

    private async void BtnRedial_Click(object sender, RoutedEventArgs e)
    {
        var password = GetRouterPassword();
        if (string.IsNullOrEmpty(password))
        {
            var stored = CredentialStore.LoadPassword();
            if (!string.IsNullOrEmpty(stored))
            {
                SetRouterPassword(stored);
                ChkRemember.IsChecked = true;
                password = stored;
            }
        }
        if (string.IsNullOrEmpty(password))
        {
            MessageBox.Show(this, "请先填写路由器管理密码。", "NetHub");
            return;
        }

        if (MessageBox.Show(this, "将触发 PPPoE 重拨以更换公网 IP。\n中断约 10~30 秒，确认？", "确认重拨",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        SetBusy(true);
        try
        {
            AppendLog("获取当前公网 IP…");
            var oldIp = await NetworkService.GetPublicIpAsync();
            AppendLog($"重拨前: {oldIp ?? "检测失败"}");

            AppendLog("登录路由器…");
            var router = new MiRouterClient();
            string token;
            try
            {
                token = await router.LoginAsync(password);
            }
            catch (Exception ex)
            {
                AppendLog($"登录失败: {ex.Message}", "err");
                return;
            }

            AppendLog("登录成功，读取 PPPoE…", "ok");
            var (user, pwd) = await router.GetPppoeCredentialsAsync(token);
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pwd))
            {
                AppendLog("未读取到 PPPoE 信息（可能非拨号模式）", "err");
                return;
            }

            var maskUser = user.Length <= 4 ? user : user[..4] + "****";
            AppendLog($"PPPoE: {maskUser} · 触发重拨…");
            await router.RedialPppoeAsync(token, user, pwd);
            AppendLog("已接受，等待拨号…", "warn");

            string? newIp = null;
            string? probe = null;
            for (var i = 0; i < 12; i++)
            {
                await Task.Delay(5000);
                probe = await NetworkService.GetPublicIpAsync();
                if (probe is not null && probe != oldIp)
                {
                    newIp = probe;
                    break;
                }
            }

            if (newIp is not null)
            {
                TxtPublic.Text = newIp;
                TxtPublicSub.Text = $"已更换 {oldIp} → {newIp}";
                AppendLog($"公网 IP: {oldIp} → {newIp}", "ok");
            }
            else
            {
                AppendLog("60 秒内未检测到变化，可再试", "warn");
                if (probe is not null) TxtPublic.Text = probe;
            }

            if (ChkRemember.IsChecked == true)
            {
                CredentialStore.SavePassword(password);
                AppendLog("路由器密码已加密保存", "ok");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"重拨出错: {ex.Message}", "err");
        }
        finally { SetBusy(false); }
    }
}
