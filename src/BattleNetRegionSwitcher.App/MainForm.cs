using BattleNetRegionSwitcher.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BattleNetRegionSwitcher.App;

internal sealed class MainForm : Form
{
    private readonly ClientLauncher launcher;
    private readonly SettingsStore store;
    private readonly DebugLog debugLog;
    private readonly LogStorage logStorage;
    private ToolSettings settings;
    private readonly TextBox pathBox = new();
    private readonly Label status = new();
    private readonly Label lastRequest = new();
    private readonly RichTextBox logBox = new();
    private readonly Label runtimeLabel = new();
    private readonly Label regionLabel = new();
    private readonly Label checkedLabel = new();
    private readonly CheckBox waitForExit = new();
    private readonly Button cancelButton;
    private readonly Button logToggle;
    private readonly LinkLabel detailLink = new();
    private readonly TableLayoutPanel layout;
    private readonly Panel logDetails = new();
    private readonly System.Windows.Forms.Timer statusTimer = new() { Interval = 2000 };
    private bool refreshing;
    private bool refreshFailureReported;
    private bool logsExpanded;
    private int logAddedHeight;
    private readonly Button browseButton;
    private readonly Button chinaButton;
    private readonly Button asiaButton;
    private bool busy;
    private bool closeAfterLaunch;
    private CancellationTokenSource? launchCancellation;
    private static readonly Color Ink = Color.FromArgb(227, 235, 247);
    private static readonly Color Muted = Color.FromArgb(154, 173, 199);

    internal MainForm(ClientLauncher launcher, SettingsStore store)
    {
        this.launcher = launcher;
        this.store = store;
        logStorage = LogStorage.Select(AppContext.BaseDirectory, store.DirectoryPath);
        debugLog = new DebugLog(logStorage.DirectoryPath);
        settings = store.Load();
        Text = $"战网地区切换 · {AppInfo.Version}";
        ClientSize = new Size(780, 724);
        MinimumSize = new Size(740, 560);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 10);
        BackColor = Color.FromArgb(15, 22, 34);
        ForeColor = Ink;

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        Controls.Add(scroll);
        layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Padding = new Padding(24), ColumnCount = 1, RowCount = 13,
            BackColor = BackColor, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        foreach (var height in new[] { 44, 30, 24, 44, 108, 148, 36, 28, 86, 34, 0, 48, 26 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        scroll.Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "选择你的战网入口", Font = new Font(Font.FontFamily, 23, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
        layout.Controls.Add(new Label { Text = "同一个客户端，按需启动国服或国际服亚洲入口。", ForeColor = Muted, Dock = DockStyle.Fill }, 0, 1);
        layout.Controls.Add(new Label { Text = "战网启动程序", Dock = DockStyle.Fill }, 0, 2);

        var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        pathBox.Dock = DockStyle.Fill;
        pathBox.Margin = new Padding(0, 3, 10, 0);
        pathBox.PlaceholderText = "未自动找到战网，请点击“选择文件”";
        pathBox.AccessibleName = "战网启动程序路径";
        pathBox.Text = ClientLauncher.ResolveLauncherPath(settings.LauncherPath) ?? "";
        pathBox.BackColor = Color.FromArgb(28, 39, 55);
        pathBox.ForeColor = Ink;
        browseButton = MakeButton("选择文件", Color.FromArgb(44, 61, 81));
        browseButton.Click += (_, _) => Browse();
        pathRow.Controls.Add(pathBox, 0, 0);
        pathRow.Controls.Add(browseButton, 1, 0);
        layout.Controls.Add(pathRow, 0, 3);

        var runtimePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = Color.FromArgb(25, 37, 52), Padding = new Padding(10, 6, 10, 6), Margin = new Padding(0, 6, 0, 0) };
        runtimePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        runtimePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        runtimePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        runtimeLabel.Dock = regionLabel.Dock = checkedLabel.Dock = DockStyle.Fill;
        runtimeLabel.Text = "正在检查战网运行状态…";
        runtimeLabel.AccessibleName = "战网运行状态";
        regionLabel.Text = "登录入口参考：正在读取配置记录…";
        regionLabel.AccessibleName = "登录入口参考";
        checkedLabel.ForeColor = Muted;
        checkedLabel.Font = new Font(Font.FontFamily, 9);
        runtimePanel.Controls.Add(runtimeLabel, 0, 0);
        runtimePanel.Controls.Add(regionLabel, 0, 1);
        runtimePanel.Controls.Add(checkedLabel, 0, 2);
        layout.Controls.Add(runtimePanel, 0, 4);

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 12, 0, 8) };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        chinaButton = MakeButton("启动国服", Color.FromArgb(20, 107, 179));
        asiaButton = MakeButton("启动国际服 · 亚洲", Color.FromArgb(27, 117, 108));
        cards.Controls.Add(MakeCard("国服", "使用国服账号登录", chinaButton, new Padding(0, 0, 8, 0)), 0, 0);
        cards.Controls.Add(MakeCard("国际服 · 亚洲", "使用国际服账号登录", asiaButton, new Padding(8, 0, 0, 0)), 1, 0);
        chinaButton.Click += async (_, _) => await LaunchAsync(LoginRegion.China);
        asiaButton.Click += async (_, _) => await LaunchAsync(LoginRegion.Asia);
        layout.Controls.Add(cards, 0, 5);

        var waitRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        waitRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        waitRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        waitForExit.Text = "等待战网及相关进程退出后启动（最多 2 分钟）";
        waitForExit.Dock = DockStyle.Fill;
        waitForExit.AccessibleName = "等待退出后启动";
        cancelButton = MakeButton("取消等待", Color.FromArgb(44, 61, 81));
        cancelButton.Enabled = false;
        cancelButton.Margin = new Padding(0, 2, 0, 4);
        cancelButton.Click += (_, _) => { launchCancellation?.Cancel(); cancelButton.Enabled = false; };
        waitRow.Controls.Add(waitForExit, 0, 0);
        waitRow.Controls.Add(cancelButton, 1, 0);
        layout.Controls.Add(waitRow, 0, 6);

        lastRequest.Dock = DockStyle.Fill;
        lastRequest.ForeColor = Muted;
        UpdateLastRequest();
        layout.Controls.Add(lastRequest, 0, 7);
        var statusPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(25, 37, 52), Padding = new Padding(10, 6, 10, 4), Margin = new Padding(0, 0, 0, 6) };
        status.Dock = DockStyle.Fill;
        status.AutoEllipsis = true;
        status.AccessibleName = "启动状态";
        status.Text = string.IsNullOrEmpty(pathBox.Text)
            ? "未自动找到战网启动程序，请点击“选择文件”。"
            : "已自动确认战网启动路径。\n请先从战网菜单正常退出，并确认游戏与更新任务已停止。";
        statusPanel.Controls.Add(status);
        detailLink.Text = "查看详情";
        detailLink.Dock = DockStyle.Bottom;
        detailLink.Height = 22;
        detailLink.LinkColor = Color.FromArgb(116, 187, 232);
        detailLink.Visible = false;
        detailLink.LinkClicked += (_, _) => SetLogsExpanded(true);
        statusPanel.Controls.Add(detailLink);
        layout.Controls.Add(statusPanel, 0, 8);

        logToggle = MakeButton("▸ 展开调试日志（后台持续记录）", Color.FromArgb(28, 39, 55));
        logToggle.TextAlign = ContentAlignment.MiddleLeft;
        logToggle.Click += (_, _) => SetLogsExpanded(!logsExpanded);
        layout.Controls.Add(logToggle, 0, 9);
        logDetails.Dock = DockStyle.Fill;
        logDetails.Margin = Padding.Empty;
        logDetails.Visible = false;
        layout.Controls.Add(logDetails, 0, 10);
        var logToolbar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 34, ColumnCount = 4, Margin = Padding.Empty };
        logToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var width in new[] { 86, 108, 100 }) logToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
        var logLocationLabel = logStorage.DirectoryPath is null ? "仅本次窗口"
            : logStorage.UsesFallback ? "用户目录 / logs（备用）" : "程序目录 / logs";
        logToolbar.Controls.Add(new Label { Text = logLocationLabel, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft });
        var copy = MakeButton("复制日志", Color.FromArgb(44, 61, 81));
        var open = MakeButton("打开日志目录", Color.FromArgb(44, 61, 81));
        var clear = MakeButton("清空视图", Color.FromArgb(44, 61, 81));
        foreach (var button in new[] { copy, open, clear }) { button.Margin = new Padding(5, 2, 0, 4); logToolbar.Controls.Add(button); }
        copy.Click += (_, _) => CopyLogs();
        open.Click += (_, _) => OpenLogs();
        clear.Click += (_, _) => { logBox.Clear(); SetStatus("已清空窗口日志视图，磁盘日志文件保留。"); };
        logBox.Dock = DockStyle.Fill;
        logBox.ReadOnly = true;
        logBox.BackColor = Color.FromArgb(10, 17, 27);
        logBox.ForeColor = Color.FromArgb(183, 211, 229);
        logBox.Font = new Font("Microsoft YaHei UI", 9);
        logBox.BorderStyle = BorderStyle.FixedSingle;
        logBox.DetectUrls = false;
        logBox.WordWrap = true;
        logBox.AccessibleName = "调试日志内容";
        logBox.Margin = new Padding(0, 3, 0, 10);
        logBox.Text = string.Join(Environment.NewLine, debugLog.Entries);
        if (logBox.TextLength > 0) logBox.AppendText(Environment.NewLine);
        logDetails.Controls.Add(logBox);
        logDetails.Controls.Add(logToolbar);
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, ForeColor = Muted, Font = new Font(Font.FontFamily, 9),
            Text = "本工具不处理账号密码，不修改游戏文件，也不提供网络加速。\n切换的是登录入口；游戏区服请在战网中确认。客户端更新后需重新验证。"
        }, 0, 11);
        var author = new LinkLabel
        {
            Text = $"作者：{AppInfo.Author}  ·  GitHub", Dock = DockStyle.Fill,
            LinkColor = Color.FromArgb(116, 187, 232), ActiveLinkColor = Color.White,
            VisitedLinkColor = Color.FromArgb(116, 187, 232), TextAlign = ContentAlignment.MiddleLeft
        };
        author.LinkClicked += (_, _) =>
        {
            try { using var process = Process.Start(new ProcessStartInfo(AppInfo.AuthorUrl) { UseShellExecute = true }); }
            catch { SetStatus("无法打开浏览器。作者主页：" + AppInfo.AuthorUrl); }
        };
        layout.Controls.Add(author, 0, 12);
        Log("INFO", $"工具启动；版本 {AppInfo.Version}；进程架构 {RuntimeInformation.ProcessArchitecture}。");
        Log("INFO", string.IsNullOrEmpty(pathBox.Text) ? "自动检测未找到战网启动程序，请选择文件。" : "已自动确认有效启动路径。点击地区按钮后会重新检查文件及运行状态。");
        Log(logStorage.UsesFallback ? "WARN" : "INFO", logStorage.DirectoryPath is null
            ? "程序目录与用户日志目录均不可写；日志仅保留在窗口，可复制反馈。"
            : logStorage.UsesFallback ? "程序目录的 logs 不可写，已回退到当前用户数据目录下的 logs。"
            : "日志保存在程序所在目录的 logs 子目录。");
        FormClosing += (_, e) =>
        {
            statusTimer.Stop();
            if (!busy) return;
            closeAfterLaunch = true;
            launchCancellation?.Cancel();
            e.Cancel = true;
            SetStatus("正在取消尚未发出的启动请求，随后关闭窗口…");
        };
        FormClosed += (_, _) => statusTimer.Dispose();
        statusTimer.Tick += async (_, _) => await RefreshRuntimeAsync();
        Shown += async (_, _) => { statusTimer.Start(); await RefreshRuntimeAsync(); };
    }

    private async Task RefreshRuntimeAsync()
    {
        if (refreshing || IsDisposed || closeAfterLaunch) return;
        refreshing = true;
        try
        {
            var snapshot = await Task.Run(() => (Runtime: launcher.ReadRuntimeStatus(), Region: new RegionReferenceReader().Read()));
            if (IsDisposed || closeAfterLaunch) return;
            runtimeLabel.Text = snapshot.Runtime.Description;
            regionLabel.Text = "登录入口参考：" + snapshot.Region.Description;
            checkedLabel.Text = $"检查于 {snapshot.Runtime.CheckedAt:HH:mm:ss}  ·  配置记录时间："
                + (snapshot.Region.ConfigModifiedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "不可用");
            refreshFailureReported = false;
        }
        catch (Exception ex)
        {
            if (IsDisposed || closeAfterLaunch) return;
            runtimeLabel.Text = "运行状态：暂时无法刷新，下次检查将重试。";
            regionLabel.Text = "登录入口参考：未知";
            checkedLabel.Text = "显示检查失败；启动前仍会重新检查。";
            if (!refreshFailureReported)
            {
                refreshFailureReported = true;
                Log("WARN", $"状态刷新失败；类型 {ex.GetType().Name}。");
            }
        }
        finally { refreshing = false; }
    }

    private void SetLogsExpanded(bool expanded)
    {
        if (logsExpanded == expanded) return;
        logsExpanded = expanded;
        var desiredHeight = (int)Math.Round(224 * DeviceDpi / 96d);
        layout.SuspendLayout();
        logDetails.Visible = expanded;
        layout.RowStyles[10].Height = expanded ? desiredHeight : 0;
        logToggle.Text = expanded ? "▾ 收起调试日志" : "▸ 展开调试日志（后台持续记录）";
        if (expanded)
        {
            var available = Math.Max(0, Screen.FromControl(this).WorkingArea.Bottom - Top - Height);
            logAddedHeight = Math.Min(desiredHeight, available);
            Height += logAddedHeight;
        }
        else { Height -= logAddedHeight; logAddedHeight = 0; }
        layout.ResumeLayout(true);
        if (expanded) logBox.Focus();
    }

    private static Button MakeButton(string text, Color color) => new()
    {
        Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = color,
        ForeColor = Color.White, Cursor = Cursors.Hand, Margin = Padding.Empty,
        FlatAppearance = { BorderSize = 0 }, UseVisualStyleBackColor = false
    };

    private Control MakeCard(string title, string detail, Button button, Padding margin)
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(28, 39, 55), Padding = new Padding(16, 10, 16, 12), Margin = margin, ColumnCount = 1, RowCount = 3 };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(new Label { Text = title, Font = new Font(Font.FontFamily, 13, FontStyle.Bold), Dock = DockStyle.Fill });
        card.Controls.Add(new Label { Text = detail, ForeColor = Muted, Dock = DockStyle.Fill });
        card.Controls.Add(button);
        return card;
    }

    private void Browse()
    {
        using var dialog = new OpenFileDialog { Title = "选择战网启动程序", Filter = "战网程序 (*.exe)|*.exe", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            pathBox.Text = ClientLauncher.ValidatePath(dialog.FileName);
            settings = settings with { LauncherPath = pathBox.Text };
            var saved = store.Save(settings);
            SetStatus(saved ? "启动路径已保存。" : "路径已选择，但未能保存设置；本次仍可使用。");
            Log(saved ? "INFO" : "WARN", saved ? "启动路径已验证并保存。" : "启动路径已验证，但设置保存失败。");
        }
        catch (Exception) { SetStatus("请选择存在的 Battle.net Launcher.exe 或 Battle.net.exe。"); Log("WARN", "所选文件不是受支持的战网启动程序，或无法访问。"); }
    }

    private async Task LaunchAsync(LoginRegion region)
    {
        if (busy) return;
        busy = true;
        launchCancellation = new CancellationTokenSource();
        var cancellation = launchCancellation.Token;
        var shouldWait = waitForExit.Checked;
        var finished = false;
        chinaButton.Enabled = asiaButton.Enabled = false;
        browseButton.Enabled = pathBox.Enabled = waitForExit.Enabled = false;
        cancelButton.Enabled = shouldWait;
        detailLink.Visible = false;
        var requestedPath = pathBox.Text.Trim();
        SetStatus("正在检查启动路径与运行状态…");
        var regionCode = region == LoginRegion.China ? "CN" : "TW";
        Log("INFO", $"收到地区启动请求：{regionCode}；开始检查路径和运行状态。");
        try
        {
            var target = region == LoginRegion.China ? "国服" : "国际服 · 亚洲";
            var lastWaitState = "";
            var progress = new Progress<WaitProgress>(p =>
            {
                if (finished || !busy || IsDisposed || closeAfterLaunch || cancellation.IsCancellationRequested) return;
                SetStatus($"等待退出后启动{target} · 剩余 {p.SecondsRemaining} 秒\n请从战网菜单选择“退出”；Agent 或游戏仍运行时会继续等待。");
                if (p.Status.Description != lastWaitState)
                {
                    lastWaitState = p.Status.Description;
                    Log("INFO", "等待退出；" + lastWaitState);
                }
            });
            if (shouldWait) Log("INFO", $"已启用等待退出后启动{target}；最多 120 秒，可取消。");
            var result = shouldWait
                ? await Task.Run(() => new WaitForExit(launcher).LaunchAsync(requestedPath, region, TimeSpan.FromMinutes(2), cancellation, progress))
                : await Task.Run(() => launcher.Launch(requestedPath, region, cancellation));
            finished = true;
            if (IsDisposed) return;
            SetStatus(result.Message);
            Log(result.Started ? "INFO" : "WARN", $"[{regionCode}] {result.Message}");
            detailLink.Visible = !result.Started && !cancellation.IsCancellationRequested;
            if (result.Started)
            {
                settings = new ToolSettings(Path.GetFullPath(requestedPath), regionCode);
                if (!store.Save(settings)) { SetStatus(status.Text + "\n本次设置未能保存。"); Log("WARN", "启动请求已发送，但工具设置保存失败。"); }
                UpdateLastRequest();
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed) { SetStatus("未能完成启动检查。请确认战网已正常退出，然后重试。"); Log("ERROR", $"[{regionCode}] 启动流程异常；类型 {ex.GetType().Name}。"); detailLink.Visible = true; }
        }
        finally
        {
            finished = true;
            busy = false;
            launchCancellation?.Dispose();
            launchCancellation = null;
            if (!IsDisposed)
            {
                chinaButton.Enabled = asiaButton.Enabled = browseButton.Enabled = pathBox.Enabled = waitForExit.Enabled = true;
                cancelButton.Enabled = false;
            }
            if (closeAfterLaunch && !IsDisposed) Close();
        }
    }

    private void SetStatus(string text) { status.Text = text; status.AccessibleName = "启动状态：" + text; }

    private void Log(string level, string message)
    {
        var line = debugLog.Write(level, message);
        if (logBox.Lines.Length >= 300) logBox.Text = string.Join(Environment.NewLine, logBox.Lines.TakeLast(250)) + Environment.NewLine;
        logBox.AppendText(line + Environment.NewLine);
        logBox.SelectionStart = logBox.TextLength;
        logBox.ScrollToCaret();
        const string writeFailure = "日志文件写入失败，本次日志仍可在窗口复制。";
        if (!debugLog.LastWriteSucceeded && !status.Text.Contains(writeFailure))
            SetStatus(status.Text + "\n" + writeFailure);
    }

    private void CopyLogs()
    {
        try { if (logBox.TextLength > 0) { Clipboard.SetText(logBox.Text); SetStatus("当前显示的日志已复制。"); } else SetStatus("当前视图没有日志。"); }
        catch (ExternalException) { SetStatus("剪贴板正被占用，请稍后重试。"); }
    }

    private void OpenLogs()
    {
        try
        {
            if (logStorage.DirectoryPath is null)
            {
                SetStatus("没有可写的日志目录，请使用“复制日志”保存本次记录。");
                return;
            }
            Directory.CreateDirectory(logStorage.DirectoryPath);
            var actualDirectory = ShellDirectory.ResolvePath(logStorage.DirectoryPath);
            using var process = Process.Start(new ProcessStartInfo(actualDirectory) { UseShellExecute = true });
            SetStatus("已请求打开日志目录。");
            Log("INFO", "已解析实际日志目录并请求资源管理器打开。");
        }
        catch (Exception ex)
        {
            SetStatus("无法打开日志目录。可先复制窗口日志用于反馈。");
            Log("ERROR", $"打开日志目录失败；类型 {ex.GetType().Name}。");
        }
    }

    private void UpdateLastRequest() => lastRequest.Text = settings.LastRequestedRegion switch
    {
        "CN" => "上次请求：国服（不代表当前登录状态）",
        "TW" => "上次请求：国际服亚洲（不代表当前登录状态）",
        _ => "启动请求与实际登录结果会分别确认。"
    };
}
