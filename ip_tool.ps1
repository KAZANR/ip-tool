#requires -Version 5.1
<#
.SYNOPSIS
    IP 地址切换工具 - 图形界面
.DESCRIPTION
    为指定网卡设置静态 IP（掩码/网关/DNS），或一键切回 DHCP 自动获取。
    修改网络配置需要管理员权限，脚本会自动提权（弹出 UAC 确认框）。
.PARAMETER SkipElevation
    跳过自动提权检查（仅用于测试界面，修改操作仍需要管理员权限才能成功）。
#>
param(
    [switch]$SkipElevation
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

# ---------- 管理员权限检查 ----------
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin -and -not $SkipElevation) {
    try {
        Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$PSCommandPath`""
    } catch {
        [System.Windows.Forms.MessageBox]::Show("需要管理员权限才能修改 IP，但提权被取消了。", "IP 切换工具", 'OK', 'Warning') | Out-Null
    }
    exit
}

# ---------- 辅助函数 ----------
function Invoke-Netsh([string]$argString) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'netsh.exe'
    $psi.Arguments = $argString
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEnd() + $p.StandardError.ReadToEnd()
    $p.WaitForExit()
    [pscustomobject]@{ ExitCode = $p.ExitCode; Output = $out.Trim() }
}

function Test-IPv4([string]$text) {
    $obj = $null
    if (-not [System.Net.IPAddress]::TryParse($text, [ref]$obj)) { return $false }
    return ($obj.AddressFamily -eq 'InterNetwork')
}

function Convert-MaskToPrefix([string]$mask) {
    if (-not (Test-IPv4 $mask)) { return -1 }
    $total = 0
    foreach ($octet in $mask.Split('.')) {
        $v = [byte]$octet
        if ($v -eq 0) { continue }
        $highBits = [convert]::ToString($v, 2)
        if ($highBits -match '01') { return -1 }  # 掩码必须连续，如 255.255.255.0
        $total += ($highBits -replace '0', '').Length
    }
    return $total
}

function Get-CurrentConfigText([string]$adapterName) {
    $lines = @()
    try {
        $ad = Get-NetAdapter -Name $adapterName -ErrorAction Stop
        $lines += "状态: $($ad.Status)    速率: $($ad.LinkSpeed)"
        $ifa = $ad.ifIndex
        $ip = Get-NetIPAddress -InterfaceIndex $ifa -AddressFamily IPv4 -ErrorAction SilentlyContinue
        if ($ip) {
            $lines += "当前 IPv4: $($ip.IPAddress)/$($ip.PrefixLength)"
        } else {
            $lines += "当前 IPv4: 未获取到"
        }
        $dhcp = Get-NetIPInterface -InterfaceIndex $ifa -AddressFamily IPv4 -ErrorAction SilentlyContinue
        if ($dhcp) {
            $lines += "地址分配: $(if ($dhcp.Dhcp -eq 'Enabled') {'DHCP 自动获取'} else {'静态指定'})"
        }
        $gwObj = Get-NetRoute -InterfaceIndex $ifa -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue | Select-Object -First 1
        $lines += "网关: $(if ($gwObj) { $gwObj.NextHop } else { '无' })"
        $dns = Get-DnsClientServerAddress -InterfaceIndex $ifa -AddressFamily IPv4 -ErrorAction SilentlyContinue
        $lines += "DNS: $(if ($dns -and $dns.ServerAddresses) { $dns.ServerAddresses -join ', ' } else { '无' })"
    } catch {
        $lines += "读取失败: $($_.Exception.Message)"
    }
    return $lines -join "`r`n"
}

# ---------- 小米路由器 / 公网 IP ----------
$script:RouterIp = '192.168.31.1'

function Get-Sha1Hex([string]$text) {
    $sha = [System.Security.Cryptography.SHA1CryptoServiceProvider]::Create()
    ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text)) | ForEach-Object { $_.ToString('x2') }) -join ''
}

function Get-PublicIP {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    foreach ($u in 'http://ip.3322.net', 'http://members.3322.org/dyndns/getip', 'https://api.ipify.org') {
        try {
            $r = Invoke-WebRequest -Uri $u -UseBasicParsing -TimeoutSec 8
            $ip = $r.Content.Trim()
            if ($ip -match '^\d{1,3}(\.\d{1,3}){3}$') { return $ip }
        } catch { }
    }
    return $null
}

function Connect-MiRouter([string]$password) {
    # 从登录页动态取设备标识和盐值，再按前端同样的算法算密码摘要
    $web = Invoke-WebRequest -Uri "http://$script:RouterIp/cgi-bin/luci/web" -UseBasicParsing -TimeoutSec 8
    $devId = if ($web.Content -match "deviceId\s*=\s*'([^']+)'") { $Matches[1] } else { $null }
    $key = if ($web.Content -match "key:\s*'([0-9a-f]{32})'") { $Matches[1] } else { $null }
    if (-not $devId -or -not $key) { throw '无法从路由器获取登录参数（页面结构可能已变化）' }
    $nonce = '0_' + $devId + '_' + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() + '_' + (Get-Random -Maximum 10000)
    $inner = Get-Sha1Hex ($password + $key)
    $pwdHash = Get-Sha1Hex ($nonce + $inner)
    $resp = Invoke-RestMethod -Method Post -Uri "http://$script:RouterIp/cgi-bin/luci/api/xqsystem/login" -Body @{
        username = 'admin'; password = $pwdHash; logtype = '2'; nonce = $nonce
    } -TimeoutSec 8
    if ($resp.code -ne 0) { throw "路由器登录失败（code=$($resp.code)，管理密码可能不对）" }
    if ($resp.url -match 'stok=([0-9a-f]+)') { return $Matches[1] }
    throw '登录成功但未获取到令牌'
}

function Invoke-MiApi([string]$token, [string]$endpoint, [hashtable]$params) {
    $uri = "http://$script:RouterIp/cgi-bin/luci/;stok=$token/api/$endpoint"
    if ($params) {
        Invoke-RestMethod -Method Post -Uri $uri -Body $params -TimeoutSec 15
    } else {
        Invoke-RestMethod -Method Get -Uri $uri -TimeoutSec 15
    }
}

function Get-MiPppoeCreds([string]$token) {
    # 从路由器状态里原样读出 PPPoE 账号密码（不经过工具保存）
    $inf = Invoke-MiApi $token 'xqsystem/information'
    $json = $inf | ConvertTo-Json -Depth 20 -Compress
    $user = if ($json -match '"username"\s*:\s*"([^"]+)"') { $Matches[1] } else { $null }
    $pwd = if ($json -match '"password"\s*:\s*"([^"]+)"') { $Matches[1] } else { $null }
    [pscustomobject]@{ User = $user; Password = $pwd }
}


# ---------- 界面 ----------
$form = New-Object System.Windows.Forms.Form
$form.Text = 'IP 地址切换工具'
$form.Size = New-Object System.Drawing.Size(560, 690)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 9)

function New-Label([string]$text, [int]$x, [int]$y) {
    $l = New-Object System.Windows.Forms.Label
    $l.Text = $text
    $l.Location = New-Object System.Drawing.Point($x, $y)
    $l.AutoSize = $true
    $form.Controls.Add($l)
    return $l
}

function New-TextBox([int]$x, [int]$y) {
    $t = New-Object System.Windows.Forms.TextBox
    $t.Location = New-Object System.Drawing.Point($x, $y)
    $t.Size = New-Object System.Drawing.Size(180, 24)
    $form.Controls.Add($t)
    return $t
}

New-Label '网络适配器:' 20 20 | Out-Null
$cmbAdapter = New-Object System.Windows.Forms.ComboBox
$cmbAdapter.Location = New-Object System.Drawing.Point(110, 16)
$cmbAdapter.Size = New-Object System.Drawing.Size(340, 24)
$cmbAdapter.DropDownStyle = 'DropDownList'
$form.Controls.Add($cmbAdapter)

$btnRefresh = New-Object System.Windows.Forms.Button
$btnRefresh.Text = '刷新'
$btnRefresh.Location = New-Object System.Drawing.Point(460, 15)
$btnRefresh.Size = New-Object System.Drawing.Size(70, 26)
$form.Controls.Add($btnRefresh)

New-Label '当前配置:' 20 60 | Out-Null
$txtInfo = New-Object System.Windows.Forms.TextBox
$txtInfo.Location = New-Object System.Drawing.Point(110, 55)
$txtInfo.Size = New-Object System.Drawing.Size(420, 90)
$txtInfo.Multiline = $true
$txtInfo.ReadOnly = $true
$txtInfo.ScrollBars = 'Vertical'
$txtInfo.BackColor = [System.Drawing.Color]::White
$form.Controls.Add($txtInfo)

New-Label 'IP 地址:' 20 165 | Out-Null
$txtIP = New-TextBox 110 162

New-Label '子网掩码:' 20 200 | Out-Null
$txtMask = New-TextBox 110 197
$txtMask.Text = '255.255.255.0'

New-Label '默认网关:' 20 235 | Out-Null
$txtGateway = New-TextBox 110 232

New-Label '首选 DNS:' 20 270 | Out-Null
$txtDns1 = New-TextBox 110 267

New-Label '备用 DNS:' 20 305 | Out-Null
$txtDns2 = New-TextBox 110 302

New-Label '(网关和 DNS 留空 = 不设置)' 300 235 | Out-Null

$btnStatic = New-Object System.Windows.Forms.Button
$btnStatic.Text = '应用静态 IP'
$btnStatic.Location = New-Object System.Drawing.Point(110, 345)
$btnStatic.Size = New-Object System.Drawing.Size(180, 36)
$btnStatic.BackColor = [System.Drawing.Color]::FromArgb(0, 120, 215)
$btnStatic.ForeColor = [System.Drawing.Color]::White
$btnStatic.FlatStyle = 'Flat'
$form.Controls.Add($btnStatic)

$btnDhcp = New-Object System.Windows.Forms.Button
$btnDhcp.Text = '切换为 DHCP 自动获取'
$btnDhcp.Location = New-Object System.Drawing.Point(310, 345)
$btnDhcp.Size = New-Object System.Drawing.Size(180, 36)
$form.Controls.Add($btnDhcp)

# ---- 公网 IP 区域 ----
New-Label '公网 IPv4:' 20 402 | Out-Null
$txtPublic = New-Object System.Windows.Forms.TextBox
$txtPublic.Location = New-Object System.Drawing.Point(110, 399)
$txtPublic.Size = New-Object System.Drawing.Size(180, 24)
$txtPublic.ReadOnly = $true
$form.Controls.Add($txtPublic)

$btnDetectPub = New-Object System.Windows.Forms.Button
$btnDetectPub.Text = '检测公网 IP'
$btnDetectPub.Location = New-Object System.Drawing.Point(300, 398)
$btnDetectPub.Size = New-Object System.Drawing.Size(120, 28)
$form.Controls.Add($btnDetectPub)

$btnRedial = New-Object System.Windows.Forms.Button
$btnRedial.Text = '重拨换新 IP'
$btnRedial.Location = New-Object System.Drawing.Point(430, 398)
$btnRedial.Size = New-Object System.Drawing.Size(110, 28)
$form.Controls.Add($btnRedial)

New-Label '路由器密码:' 20 437 | Out-Null
$txtRouterPwd = New-Object System.Windows.Forms.TextBox
$txtRouterPwd.Location = New-Object System.Drawing.Point(110, 434)
$txtRouterPwd.Size = New-Object System.Drawing.Size(180, 24)
$txtRouterPwd.PasswordChar = '*'
$form.Controls.Add($txtRouterPwd)

$chkRememberPwd = New-Object System.Windows.Forms.CheckBox
$chkRememberPwd.Text = '记住密码（本机加密存储）'
$chkRememberPwd.Location = New-Object System.Drawing.Point(300, 434)
$chkRememberPwd.AutoSize = $true
$form.Controls.Add($chkRememberPwd)

New-Label '操作结果:' 20 477 | Out-Null
$txtLog = New-Object System.Windows.Forms.TextBox
$txtLog.Location = New-Object System.Drawing.Point(110, 472)
$txtLog.Size = New-Object System.Drawing.Size(420, 130)
$txtLog.Multiline = $true
$txtLog.ReadOnly = $true
$txtLog.ScrollBars = 'Vertical'
$form.Controls.Add($txtLog)

$statusLabel = New-Object System.Windows.Forms.Label
$statusLabel.Location = New-Object System.Drawing.Point(20, 616)
$statusLabel.AutoSize = $true
$statusLabel.ForeColor = [System.Drawing.Color]::Gray
$form.Controls.Add($statusLabel)
if (-not $isAdmin) {
    $statusLabel.Text = '⚠ 当前未以管理员身份运行，修改操作将失败'
    $statusLabel.ForeColor = [System.Drawing.Color]::DarkRed
} else {
    $statusLabel.Text = '已以管理员身份运行'
}

function Get-AdapterList {
    Get-NetAdapter | Sort-Object Status -Descending | ForEach-Object {
        [pscustomobject]@{ Name = $_.Name; Desc = $_.InterfaceDescription; Status = $_.Status; ifIndex = $_.ifIndex }
    }
}

function Refresh-Adapters {
    $selected = $cmbAdapter.Text
    $cmbAdapter.Items.Clear()
    $adapters = Get-AdapterList
    foreach ($a in $adapters) {
        $cmbAdapter.Items.Add("$($a.Name)  [$($a.Status)]") | Out-Null
    }
    if ($selected) {
        for ($i = 0; $i -lt $cmbAdapter.Items.Count; $i++) {
            if ($cmbAdapter.Items[$i].StartsWith($selected + '  ')) { $cmbAdapter.SelectedIndex = $i; break }
        }
    } elseif ($cmbAdapter.Items.Count -gt 0) {
        # 优先选中已连接的网卡
        for ($i = 0; $i -lt $cmbAdapter.Items.Count; $i++) {
            if ($cmbAdapter.Items[$i] -match '\[Up\]') { $cmbAdapter.SelectedIndex = $i; break }
        }
        if ($cmbAdapter.SelectedIndex -lt 0) { $cmbAdapter.SelectedIndex = 0 }
    }
}

function Get-SelectedAdapterName {
    if ($cmbAdapter.SelectedIndex -lt 0) { return $null }
    return ($cmbAdapter.SelectedItem -replace '\s+\[.*\]$', '')
}

function Show-CurrentConfig {
    $name = Get-SelectedAdapterName
    if (-not $name) { return }
    $txtInfo.Text = Get-CurrentConfigText $name
    if (-not $txtIP.Text.Trim()) {
        $ad = Get-NetAdapter -Name $name -ErrorAction SilentlyContinue
        if ($ad) {
            $cur = Get-NetIPAddress -InterfaceIndex $ad.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($cur) { $txtIP.Text = $cur.IPAddress }
        }
    }
}

function Append-Log([string]$text) {
    $txtLog.AppendText("[$(Get-Date -Format 'HH:mm:ss')] $text`r`n")
    $txtLog.ScrollToCaret()
}

$cmbAdapter.Add_SelectedIndexChanged({ Show-CurrentConfig })
$btnRefresh.Add_Click({
    Refresh-Adapters
    Show-CurrentConfig
})
$form.Add_Shown({
    Refresh-Adapters
    Show-CurrentConfig
})

$btnStatic.Add_Click({
    $name = Get-SelectedAdapterName
    if (-not $name) {
        [System.Windows.Forms.MessageBox]::Show('请先选择一个网络适配器。', '提示') | Out-Null
        return
    }
    $ip = $txtIP.Text.Trim()
    $mask = $txtMask.Text.Trim()
    $gw = $txtGateway.Text.Trim()
    $dns1 = $txtDns1.Text.Trim()
    $dns2 = $txtDns2.Text.Trim()

    if (-not (Test-IPv4 $ip)) {
        [System.Windows.Forms.MessageBox]::Show("IP 地址格式不正确: $ip", '校验失败') | Out-Null
        return
    }
    $prefix = Convert-MaskToPrefix $mask
    if ($prefix -lt 0) {
        [System.Windows.Forms.MessageBox]::Show("子网掩码格式不正确: $mask", '校验失败') | Out-Null
        return
    }
    if ($gw -and -not (Test-IPv4 $gw)) {
        [System.Windows.Forms.MessageBox]::Show("默认网关格式不正确: $gw", '校验失败') | Out-Null
        return
    }
    if ($dns1 -and -not (Test-IPv4 $dns1)) {
        [System.Windows.Forms.MessageBox]::Show("首选 DNS 格式不正确: $dns1", '校验失败') | Out-Null
        return
    }
    if ($dns2 -and -not (Test-IPv4 $dns2)) {
        [System.Windows.Forms.MessageBox]::Show("备用 DNS 格式不正确: $dns2", '校验失败') | Out-Null
        return
    }

    $confirm = "即将把网卡 [$name] 设置为:`r`nIP: $ip`r`n掩码: $mask ($prefix 位前缀)"
    if ($gw) { $confirm += "`r`n网关: $gw" }
    if ($dns1) { $confirm += "`r`nDNS: $dns1" + $(if ($dns2) { " / $dns2" }) }
    $confirm += "`r`n`r`n确认应用？远程桌面/SSH 场景下改错网关可能导致断连。"
    if ([System.Windows.Forms.MessageBox]::Show($confirm, '确认修改', 'OKCancel', 'Question') -ne 'OK') { return }

    $btnStatic.Enabled = $false
    $btnDhcp.Enabled = $false
    try {
        if ($gw) {
            $r = Invoke-Netsh "interface ip set address name=`"$name`" source=static addr=$ip mask=$mask gateway=$gw gwmetric=1"
        } else {
            $r = Invoke-Netsh "interface ip set address name=`"$name`" source=static addr=$ip mask=$mask"
        }
        if ($r.ExitCode -ne 0) {
            Append-Log "设置 IP 失败: $($r.Output)"
            [System.Windows.Forms.MessageBox]::Show("设置 IP 失败:`r`n$($r.Output)", '错误') | Out-Null
            return
        }
        Append-Log "[$name] IP 已设置为 $ip/$mask" + $(if ($gw) { "，网关 $gw" })

        if ($dns1) {
            $r2 = Invoke-Netsh "interface ip set dns name=`"$name`" source=static addr=$dns1 validate=no"
            if ($r2.ExitCode -ne 0) {
                Append-Log "设置 DNS 失败: $($r2.Output)"
            } else {
                Append-Log "[$name] 首选 DNS 已设置为 $dns1"
                if ($dns2) {
                    $r3 = Invoke-Netsh "interface ip add dns name=`"$name`" addr=$dns2 index=2 validate=no"
                    if ($r3.ExitCode -ne 0) {
                        Append-Log "设置备用 DNS 失败: $($r3.Output)"
                    } else {
                        Append-Log "[$name] 备用 DNS 已设置为 $dns2"
                    }
                }
            }
        } else {
            Append-Log 'DNS 未填写，保持原样未改动。'
        }
        Show-CurrentConfig
    } finally {
        $btnStatic.Enabled = $true
        $btnDhcp.Enabled = $true
    }
})

$btnDhcp.Add_Click({
    $name = Get-SelectedAdapterName
    if (-not $name) {
        [System.Windows.Forms.MessageBox]::Show('请先选择一个网络适配器。', '提示') | Out-Null
        return
    }
    if ([System.Windows.Forms.MessageBox]::Show("即将把网卡 [$name] 切回 DHCP 自动获取 IP 和 DNS，确认？", '确认修改', 'OKCancel', 'Question') -ne 'OK') { return }

    $btnStatic.Enabled = $false
    $btnDhcp.Enabled = $false
    try {
        $r = Invoke-Netsh "interface ip set address name=`"$name`" source=dhcp"
        if ($r.ExitCode -ne 0) {
            Append-Log "切回 DHCP 失败: $($r.Output)"
            [System.Windows.Forms.MessageBox]::Show("切回 DHCP 失败:`r`n$($r.Output)", '错误') | Out-Null
            return
        }
        Append-Log "[$name] IP 已切换为 DHCP 自动获取"
        $r2 = Invoke-Netsh "interface ip set dns name=`"$name`" source=dhcp"
        if ($r2.ExitCode -eq 0) {
            Append-Log "[$name] DNS 已切换为 DHCP 自动获取"
        } else {
            Append-Log "DNS 切回 DHCP 失败: $($r2.Output)"
        }
        Show-CurrentConfig
    } finally {
        $btnStatic.Enabled = $true
        $btnDhcp.Enabled = $true
    }
})

# ---- 公网 IP 检测与重拨 ----
$script:CredFile = Join-Path $env:LOCALAPPDATA 'MiRouterCred.xml'

function Save-RouterPassword([string]$plain) {
    $plain | ConvertTo-SecureString -AsPlainText -Force | ConvertFrom-SecureString | Set-Content -Path $script:CredFile -Encoding UTF8
}

function Get-StoredRouterPassword {
    if (-not (Test-Path $script:CredFile)) { return $null }
    try {
        $sec = Get-Content $script:CredFile | ConvertTo-SecureString
        $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    } catch { return $null }
}

$btnDetectPub.Add_Click({
    $btnDetectPub.Enabled = $false
    $txtPublic.Text = '检测中...'
    try {
        $ip = Get-PublicIP
        if ($ip) {
            $txtPublic.Text = $ip
            Append-Log "当前公网 IPv4: $ip"
        } else {
            $txtPublic.Text = ''
            Append-Log '公网 IP 检测失败（无法访问检测服务，检查网络）'
        }
    } finally {
        $btnDetectPub.Enabled = $true
    }
})

$btnRedial.Add_Click({
    $routerPwd = $txtRouterPwd.Text
    if (-not $routerPwd) { $routerPwd = Get-StoredRouterPassword; if ($routerPwd) { $chkRememberPwd.Checked = $true } }
    if (-not $routerPwd) {
        [System.Windows.Forms.MessageBox]::Show('请先填写路由器管理密码（登录 192.168.31.1 管理页用的那个）。', '提示') | Out-Null
        return
    }
    if ([System.Windows.Forms.MessageBox]::Show("将通知路由器重新 PPPoE 拨号以更换公网 IP。`r`n重拨期间全家断网约 10~30 秒，确认继续？", '确认重拨', 'OKCancel', 'Question') -ne 'OK') { return }

    $btnRedial.Enabled = $false
    $btnDetectPub.Enabled = $false
    try {
        Append-Log '正在获取当前公网 IP...'
        $oldIp = Get-PublicIP
        Append-Log "重拨前公网 IP: $(if ($oldIp) { $oldIp } else { '检测失败，继续尝试' })"

        Append-Log '正在登录路由器...'
        $token = $null
        try { $token = Connect-MiRouter $routerPwd } catch { Append-Log "登录失败: $($_.Exception.Message)"; return }
        Append-Log '登录成功，读取 PPPoE 配置...'

        $creds = Get-MiPppoeCreds $token
        if (-not $creds.User -or -not $creds.Password) {
            Append-Log '未能从路由器读取到 PPPoE 账号信息（可能不是 PPPoE 拨号模式），已中止。'
            return
        }
        Append-Log "PPPoE 账号: $($creds.User.Substring(0, [Math]::Min(4, $creds.User.Length)))****  触发重拨..."

        $r = Invoke-MiApi $token 'xqnetwork/set_wan' @{ wanType = 'pppoe'; pppoeName = $creds.User; pppoePwd = $creds.Password }
        if ($r.code -ne 0) {
            Append-Log "重拨指令被路由器拒绝（code=$($r.code) msg=$($r.msg)）。请到路由器管理页手动断开/重连，并把此信息反馈给开发者。"
            return
        }
        Append-Log '重拨指令已接受，等待重新拨号（约 10~30 秒）...'

        # 轮询等待新的公网 IP 出现
        $newIp = $null
        for ($i = 0; $i -lt 12; $i++) {
            Start-Sleep -Seconds 5
            $probe = Get-PublicIP
            if ($probe -and $probe -ne $oldIp) { $newIp = $probe; break }
        }
        if ($newIp) {
            $txtPublic.Text = $newIp
            Append-Log "公网 IP 已更换: $oldIp → $newIp"
        } else {
            Append-Log "重拨已执行，但 60 秒内未检测到公网 IP 变化（可能抽到了同一个地址），可再点一次。"
            if ($probe) { $txtPublic.Text = $probe }
        }

        if ($chkRememberPwd.Checked) { Save-RouterPassword $routerPwd }
    } catch {
        Append-Log "重拨过程出错: $($_.Exception.Message)"
    } finally {
        $btnRedial.Enabled = $true
        $btnDetectPub.Enabled = $true
    }
})

# 启动时若存过密码则自动填充并检测一次公网 IP
$form.Add_Shown({
    $stored = Get-StoredRouterPassword
    if ($stored) {
        $txtRouterPwd.Text = $stored
        $chkRememberPwd.Checked = $true
    }
})

# 进入消息循环（仅当本进程就是 GUI 进程时）
if ($SkipElevation -or $isAdmin) {
    [void]$form.ShowDialog()
}
