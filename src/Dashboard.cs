using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class Dashboard : Form
{
    internal static readonly Color Background = Color.FromArgb(11, 18, 32);
    internal static readonly Color Sidebar = Color.FromArgb(16, 27, 45);
    internal static readonly Color Surface = Color.FromArgb(22, 35, 55);
    internal static readonly Color Muted = Color.FromArgb(151, 170, 194);
    internal static readonly Color Foreground = Color.FromArgb(235, 244, 255);
    internal static readonly Color Cyan = Color.FromArgb(52, 211, 224);
    internal static readonly Color Violet = Color.FromArgb(175, 155, 248);
    internal static readonly Color Mint = Color.FromArgb(100, 219, 167);
    private readonly bool previewMode;
    private readonly PerformanceTelemetry telemetry;
    private readonly System.Windows.Forms.Timer timer;
    private readonly List<TelemetrySample> history = new List<TelemetrySample>();
    private readonly Panel pageHost = new Panel();
    private readonly Panel[] pages = new Panel[3];
    private readonly RoundedButton[] navigation = new RoundedButton[3];
    private readonly Label title = new Label();
    private readonly Label status = new Label();
    private readonly Label sensorStatus = new Label();
    private readonly Label errorsStatus = new Label();
    private readonly Label profileSummary = new Label();
    private readonly RoundedButton openLog = new RoundedButton();
    private readonly ThinProgress operationProgress = new ThinProgress();
    private VendorSession vendorSession;
    private bool sessionPolling;
    private bool operationActive;
    private int operationErrors;
    private string operationStage = "Ready";
    private RunLogWindow logWindow;
    private readonly RoundedButton fullWinUtil = new RoundedButton();
    private readonly MetricTile overviewCpu, overviewRam, overviewGpu;
    private readonly MetricTile liveCpu, liveRam, liveGpu, liveDisk, liveNetwork, liveGpuPower;
    private readonly UtilizationChart chart = new UtilizationChart();
    private readonly Label notes = new Label();
    private readonly Label driveStatus = new Label();
    private readonly DataGridView drives = new DataGridView();
    private readonly DataGridView comparisons = new DataGridView();
    private readonly ComboBox preset = new ComboBox();
    private readonly TextBox config = new TextBox();
    private readonly CheckBox offline = new CheckBox();
    private readonly TextBox workload = new TextBox();
    private readonly Label captureStatus = new Label();
    private readonly ThinProgress captureProgress = new ThinProgress();
    private readonly RoundedButton captureBefore = new RoundedButton();
    private readonly RoundedButton captureAfter = new RoundedButton();
    private readonly RoundedButton applyProfile = new RoundedButton();
    private bool sampling;
    private bool capturing;
    private bool captureIsBefore;
    private DateTime captureStart;
    private List<TelemetrySample> captureSamples;
    private CaptureSnapshot before;
    private CaptureSnapshot after;

    internal Dashboard(bool preview)
    {
        previewMode = preview;
        telemetry = new PerformanceTelemetry();
        Text = "WinUtil Performance | Community companion";
        BackColor = Background;
        ForeColor = Foreground;
        Font = new Font("Segoe UI", 10F);
        using (Stream iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinUtil.Icon"))
            if (iconStream != null) Icon = new Icon(iconStream);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1180, 780);
        MinimumSize = new Size(1050, 680);
        StartPosition = FormStartPosition.CenterScreen;

        TableLayoutPanel frame = Table(2, 1);
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 204));
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(frame);
        Panel side = new Panel { Dock = DockStyle.Fill, BackColor = Sidebar, Padding = new Padding(20, 26, 18, 18), Margin = new Padding(0) };
        frame.Controls.Add(side, 0, 0);
        TableLayoutPanel sideLayout = Table(1, 6);
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        side.Controls.Add(sideLayout);
        Label brand = LabelText("WINUTIL\nPERFORMANCE", 12, Foreground, FontStyle.Bold);
        brand.Dock = DockStyle.Fill;
        sideLayout.Controls.Add(brand, 0, 0);
        string[] navigationText = { "Overview", "Live monitor", "Before & after" };
        for (int index = 0; index < 3; index++)
        {
            int selected = index;
            navigation[index] = Button(navigationText[index], Surface, Foreground);
            navigation[index].TextAlign = ContentAlignment.MiddleLeft;
            navigation[index].Margin = new Padding(0, 4, 0, 4);
            navigation[index].Click += delegate { SelectPage(selected); };
            sideLayout.Controls.Add(navigation[index], 0, index + 1);
        }
        Label footer = LabelText("Community companion\nWinUtil 26.10.07\nRead-only monitoring", 8, Muted);
        footer.Dock = DockStyle.Fill;
        sideLayout.Controls.Add(footer, 0, 5);

        Panel main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 10), Margin = new Padding(0) };
        frame.Controls.Add(main, 1, 0);
        TableLayoutPanel mainLayout = Table(1, 3);
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        main.Controls.Add(mainLayout);
        TableLayoutPanel header = Table(2, 1);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 242));
        title.Font = new Font("Segoe UI", 24, FontStyle.Bold);
        title.ForeColor = Foreground;
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.MiddleLeft;
        title.UseMnemonic = false;
        header.Controls.Add(title, 0, 0);
        fullWinUtil.Text = "Open full WinUtil  →";
        fullWinUtil.BackColor = Cyan;
        fullWinUtil.ForeColor = Background;
        fullWinUtil.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        fullWinUtil.Dock = DockStyle.Fill;
        fullWinUtil.Margin = new Padding(0, 17, 0, 14);
        fullWinUtil.Click += delegate { OpenVendor(new Launcher.LaunchOptions()); };
        header.Controls.Add(fullWinUtil, 1, 0);
        mainLayout.Controls.Add(header, 0, 0);
        pageHost.Dock = DockStyle.Fill;
        pageHost.Margin = new Padding(0);
        mainLayout.Controls.Add(pageHost, 0, 1);
        mainLayout.Controls.Add(BuildStatusBar(), 0, 2);

        overviewCpu = new MetricTile("CPU ACTIVITY", Cyan);
        overviewRam = new MetricTile("MEMORY IN USE", Violet);
        overviewGpu = new MetricTile("GPU ACTIVITY", Mint);
        liveCpu = new MetricTile("CPU", Cyan, true);
        liveRam = new MetricTile("MEMORY", Violet, true);
        liveGpu = new MetricTile("GPU", Mint, true);
        liveDisk = new MetricTile("DISK READ / WRITE", Cyan, true);
        liveNetwork = new MetricTile("NETWORK RECEIVE / SEND", Violet, true);
        liveGpuPower = new MetricTile("GPU TEMPERATURE / POWER", Mint, true);
        pages[0] = BuildOverview();
        pages[1] = BuildMonitor();
        pages[2] = BuildComparisons();
        foreach (Panel page in pages) { page.Dock = DockStyle.Fill; pageHost.Controls.Add(page); }
        SelectPage(0);
        if (!previewMode) LoadSavedCaptures();

        timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += delegate { PollVendorSession(); RequestSample(); operationProgress.AdvanceAnimation(); if (logWindow != null && !logWindow.IsDisposed) logWindow.RefreshLog(); };
        if (!previewMode)
        {
            Shown += delegate { RequestSample(); timer.Start(); };
        }
        FormClosed += delegate { timer.Stop(); timer.Dispose(); telemetry.Dispose(); if (vendorSession != null) vendorSession.Dispose(); };
    }

    private Control BuildStatusBar()
    {
        RoundedCard bar = new RoundedCard { Padding = new Padding(13, 8, 13, 7), Margin = new Padding(0, 10, 0, 0) };
        TableLayoutPanel layout = Table(3, 3);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 5));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        status.Text = "Ready  ·  Choose a profile or open full WinUtil.";
        status.ForeColor = Foreground; status.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.AutoEllipsis = true; status.UseMnemonic = false;
        layout.Controls.Add(status, 0, 0);
        errorsStatus.Text = "Errors: 0"; errorsStatus.ForeColor = Muted; errorsStatus.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        errorsStatus.Dock = DockStyle.Fill; errorsStatus.TextAlign = ContentAlignment.MiddleCenter;
        layout.Controls.Add(errorsStatus, 1, 0);
        openLog.Text = "Show log"; openLog.BackColor = Sidebar; openLog.ForeColor = Cyan;
        openLog.Font = new Font("Segoe UI", 9, FontStyle.Bold); openLog.Dock = DockStyle.Fill; openLog.Margin = new Padding(3, 0, 0, 1); openLog.Enabled = false;
        openLog.Click += delegate { ShowRunLog(); };
        layout.Controls.Add(openLog, 2, 0);
        operationProgress.Dock = DockStyle.Fill; operationProgress.Margin = new Padding(0, 2, 0, 1);
        layout.Controls.Add(operationProgress, 0, 1); layout.SetColumnSpan(operationProgress, 3);
        sensorStatus.Text = "Sensors: waiting for readings  ·  Read-only monitoring";
        sensorStatus.ForeColor = Muted; sensorStatus.Font = new Font("Segoe UI", 8); sensorStatus.Dock = DockStyle.Fill;
        sensorStatus.TextAlign = ContentAlignment.MiddleLeft; sensorStatus.AutoEllipsis = true;
        layout.Controls.Add(sensorStatus, 0, 2); layout.SetColumnSpan(sensorStatus, 3);
        bar.Controls.Add(layout);
        return bar;
    }

    private void SetOperationStatus(string stage, string message, bool active, int errorCount)
    {
        operationStage = stage; operationActive = active; operationErrors = Math.Max(0, errorCount);
        status.Text = stage + "  ·  " + message;
        errorsStatus.Text = "Errors: " + operationErrors.ToString(CultureInfo.InvariantCulture);
        errorsStatus.ForeColor = operationErrors > 0 ? Color.FromArgb(255, 159, 150) : Muted;
        operationProgress.IsIndeterminate = active;
        operationProgress.Value = active ? 0 : (stage == "Ready" || stage == "Canceled" ? 0 : 100);
    }

    private void SetActivityStatus(string message)
    {
        // A capture or export must never replace a running WinUtil operation.
        if (!operationActive) SetOperationStatus("Ready", message, false, operationErrors);
    }

    private Panel BuildOverview()
    {
        Panel page = new Panel { AutoScroll = true };
        TableLayoutPanel layout = Table(1, 3);
        MakeScrollable(page, layout, 601);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 93));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);
        RoundedCard hero = new RoundedCard();
        hero.Margin = new Padding(0, 0, 0, 12);
        TableLayoutPanel copy = Table(1, 2);
        copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        copy.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        copy.Controls.Add(LabelText("The original toolkit. A clearer view of your PC.", 15, Foreground, FontStyle.Bold), 0, 0);
        copy.Controls.Add(LabelText("Open WinUtil for all vendor options. Watch real activity here, then compare the same workload before and after.", 10, Muted), 0, 1);
        hero.Controls.Add(copy);
        layout.Controls.Add(hero, 0, 0);
        TableLayoutPanel metrics = Table(3, 1);
        for (int index = 0; index < 3; index++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        metrics.Controls.Add(overviewCpu, 0, 0);
        metrics.Controls.Add(overviewRam, 1, 0);
        metrics.Controls.Add(overviewGpu, 2, 0);
        layout.Controls.Add(metrics, 0, 1);
        TableLayoutPanel bottom = Table(2, 1);
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        bottom.Controls.Add(BuildAutomation(), 0, 0);
        bottom.Controls.Add(BuildExpectations(), 1, 0);
        layout.Controls.Add(bottom, 0, 2);
        return page;
    }

    private RoundedCard BuildAutomation()
    {
        RoundedCard card = new RoundedCard { Margin = new Padding(0, 0, 7, 0) };
        TableLayoutPanel layout = Table(1, 9);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 63));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        layout.Controls.Add(LabelText("Run a chosen profile", 15, Foreground, FontStyle.Bold), 0, 0);
        layout.Controls.Add(LabelText("Applies the selected profile after administrator approval.", 9.5F, Muted), 0, 1);
        layout.Controls.Add(LabelText("PRESET", 8.5F, Muted, FontStyle.Bold), 0, 2);
        preset.DropDownStyle = ComboBoxStyle.DropDownList;
        preset.Items.AddRange(new object[] { "None — choose in full WinUtil", "Gaming — Windows Game Mode", "Standard", "Minimal", "Advanced" });
        preset.SelectedIndex = 0;
        preset.Font = new Font("Segoe UI", 10);
        preset.DrawMode = DrawMode.OwnerDrawFixed;
        preset.FlatStyle = FlatStyle.Flat;
        preset.ItemHeight = 24;
        preset.DrawItem += delegate(object sender, DrawItemEventArgs e) {
            using (Brush background = new SolidBrush(Sidebar)) e.Graphics.FillRectangle(background, e.Bounds);
            string selection = e.Index < 0 ? (preset.SelectedItem == null ? "Choose preset" : preset.SelectedItem.ToString()) : preset.Items[e.Index].ToString();
            TextRenderer.DrawText(e.Graphics, selection, preset.Font, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height), Foreground, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        };
        preset.BackColor = Sidebar;
        preset.ForeColor = Foreground;
        preset.Dock = DockStyle.Fill;
        preset.Margin = new Padding(0, 1, 0, 7);
        layout.Controls.Add(preset, 0, 3);
        profileSummary.ForeColor = Muted; profileSummary.Font = new Font("Segoe UI", 9); profileSummary.Dock = DockStyle.Fill;
        profileSummary.TextAlign = ContentAlignment.MiddleLeft; profileSummary.Margin = new Padding(0, 1, 0, 5);
        layout.Controls.Add(profileSummary, 0, 4);
        layout.Controls.Add(LabelText("CONFIG FILE OR URL  ·  OPTIONAL", 8.5F, Muted, FontStyle.Bold), 0, 5);
        TableLayoutPanel configRow = Table(2, 1);
        configRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        configRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        StyleInput(config);
        config.Dock = DockStyle.Fill;
        config.Margin = new Padding(0, 6, 7, 4);
        configRow.Controls.Add(config, 0, 0);
        RoundedButton browse = Button("Browse", Sidebar, Foreground);
        browse.Margin = new Padding(0, 2, 0, 3);
        browse.Click += delegate {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "WinUtil JSON config (*.json)|*.json|All files (*.*)|*.*", Title = "Choose WinUtil configuration" })
                if (dialog.ShowDialog(this) == DialogResult.OK) config.Text = dialog.FileName;
        };
        configRow.Controls.Add(browse, 1, 0);
        layout.Controls.Add(configRow, 0, 6);
        offline.Text = "Offline flag   ·   not a network sandbox";
        offline.ForeColor = Muted;
        offline.Font = new Font("Segoe UI", 9);
        offline.Dock = DockStyle.Top;
        offline.AutoSize = false;
        offline.Height = 28;
        offline.Margin = new Padding(0);
        layout.Controls.Add(offline, 0, 7);
        applyProfile.Text = "Apply chosen profile";
        applyProfile.Dock = DockStyle.Fill;
        applyProfile.BackColor = Color.FromArgb(38, 72, 86);
        applyProfile.ForeColor = Cyan;
        applyProfile.Margin = new Padding(0, 3, 0, 0);
        applyProfile.Click += delegate { LaunchChosenProfile(); };
        layout.Controls.Add(applyProfile, 0, 8);
        preset.SelectedIndexChanged += delegate { RefreshProfileSummary(); browse.Enabled = preset.SelectedIndex != 1; };
        config.TextChanged += delegate { RefreshProfileSummary(); };
        RefreshProfileSummary();
        card.Controls.Add(layout);
        new ToolTip().SetToolTip(config, "Optional local JSON configuration path or http(s) URL. Original WinUtil presets can combine with Config. Gaming applies Game Mode only and disables this field.");
        return card;
    }

    private static string PresetArgument(int selectedIndex)
    {
        string[] presets = { null, "Gaming", "Standard", "Minimal", "Advanced" };
        return selectedIndex >= 0 && selectedIndex < presets.Length ? presets[selectedIndex] : null;
    }

    private void RefreshProfileSummary()
    {
        bool gaming = PresetArgument(preset.SelectedIndex) == "Gaming";
        config.Enabled = !gaming;
        if (gaming)
            profileSummary.Text = "Enables Windows Game Mode only (WPFToggleGameMode). Your power plan, services, security settings, and apps stay as they are.";
        else if (preset.SelectedIndex > 1)
            profileSummary.Text = "Applies WinUtil's original " + PresetArgument(preset.SelectedIndex) + " preset" + (String.IsNullOrWhiteSpace(config.Text) ? "." : " together with your config.") + " Open full WinUtil to review individual choices.";
        else
            profileSummary.Text = String.IsNullOrWhiteSpace(config.Text) ? "No automatic changes selected. Gaming enables Game Mode; the other presets use WinUtil's original selections." : "Applies the choices in your config file or URL. Review the config before starting.";
        applyProfile.Enabled = !operationActive && (preset.SelectedIndex > 0 || !String.IsNullOrWhiteSpace(config.Text));
    }

    private RoundedCard BuildExpectations()
    {
        RoundedCard card = new RoundedCard { Margin = new Padding(7, 0, 0, 0) };
        TableLayoutPanel layout = Table(1, 6);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        for (int index = 0; index < 4; index++) layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.Controls.Add(LabelText("What changes can help?", 15, Foreground, FontStyle.Bold), 0, 0);
        string[] messages = {
            "POWER SETTINGS\nAlready at hardware limits? A plan adds no extra component headroom.",
            "GAME MODE\nCan help when background work competes for resources.",
            "VISUAL EFFECTS\nMay change how responsive Windows feels, rather than measured throughput.",
        "CLEANUP & DELIVERY CACHE\nCan free capacity or reduce peer upload traffic. Extra FPS is not guaranteed."
        };
        for (int index = 0; index < messages.Length; index++) layout.Controls.Add(LabelText(messages[index], 9, Muted), 0, index + 1);
        layout.Controls.Add(LabelText("Impact depends on your workload.", 8.5F, Cyan), 0, 5);
        card.Controls.Add(layout);
        return card;
    }

    private Panel BuildMonitor()
    {
        Panel page = new Panel { AutoScroll = true };
        TableLayoutPanel layout = Table(1, 3);
        MakeScrollable(page, layout, 570);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 224));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 177));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);
        TableLayoutPanel metrics = Table(3, 2);
        for (int index = 0; index < 3; index++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        metrics.Controls.Add(liveCpu, 0, 0); metrics.Controls.Add(liveRam, 1, 0); metrics.Controls.Add(liveGpu, 2, 0);
        metrics.Controls.Add(liveDisk, 0, 1); metrics.Controls.Add(liveNetwork, 1, 1); metrics.Controls.Add(liveGpuPower, 2, 1);
        layout.Controls.Add(metrics, 0, 0);
        chart.Dock = DockStyle.Fill;
        chart.Margin = new Padding(0, 0, 0, 10);
        layout.Controls.Add(chart, 0, 1);
        TableLayoutPanel bottom = Table(2, 1);
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        RoundedCard driveCard = new RoundedCard { Padding = new Padding(13), Margin = new Padding(0, 0, 7, 0) };
        TableLayoutPanel driveLayout = Table(1, 2);
        driveLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        driveLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        driveStatus.Text = "STORAGE CAPACITY";
        driveStatus.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        driveStatus.ForeColor = Muted;
        driveStatus.Dock = DockStyle.Fill;
        driveLayout.Controls.Add(driveStatus, 0, 0);
        StyleGrid(drives);
        drives.Columns.Add("drive", "Drive"); drives.Columns.Add("free", "Free GiB"); drives.Columns.Add("total", "Total GiB"); drives.Columns.Add("used", "Used %");
        driveLayout.Controls.Add(drives, 0, 1);
        driveCard.Controls.Add(driveLayout);
        bottom.Controls.Add(driveCard, 0, 0);
        RoundedCard noteCard = new RoundedCard { Padding = new Padding(13), Margin = new Padding(7, 0, 0, 0) };
        notes.Text = "READING STATUS\nWaiting for the first sample. Unavailable metrics appear as —.";
        notes.Font = new Font("Segoe UI", 9);
        notes.ForeColor = Muted;
        notes.Dock = DockStyle.Fill;
        noteCard.Controls.Add(notes);
        bottom.Controls.Add(noteCard, 1, 0);
        layout.Controls.Add(bottom, 0, 2);
        return page;
    }

    private Panel BuildComparisons()
    {
        Panel page = new Panel { AutoScroll = true };
        TableLayoutPanel layout = Table(1, 5);
        MakeScrollable(page, layout, 540);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
        page.Controls.Add(layout);
        RoundedCard guide = new RoundedCard { Margin = new Padding(0, 0, 0, 10), Padding = new Padding(15) };
        TableLayoutPanel guideLayout = Table(1, 2);
        guideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        guideLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        guideLayout.Controls.Add(LabelText("Measure the same workload twice", 15, Foreground, FontStyle.Bold), 0, 0);
        guideLayout.Controls.Add(LabelText("Capture Before → make changes in WinUtil → repeat the same workload → capture After.\nEach capture averages ten seconds of observed activity; use a comparable workload.", 9.5F, Muted), 0, 1);
        guide.Controls.Add(guideLayout);
        layout.Controls.Add(guide, 0, 0);
        TableLayoutPanel buttons = Table(4, 1);
        for (int index = 0; index < 4; index++) buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        captureBefore.Text = "Capture Before"; captureBefore.BackColor = Cyan; captureBefore.ForeColor = Background;
        captureAfter.Text = "Capture After"; captureAfter.BackColor = Violet; captureAfter.ForeColor = Background;
        captureBefore.Dock = captureAfter.Dock = DockStyle.Fill;
        captureBefore.Click += delegate { BeginCapture(true); };
        captureAfter.Click += delegate { BeginCapture(false); };
        RoundedButton exportJson = Button("Export JSON", Surface, Foreground);
        RoundedButton exportCsv = Button("Export CSV", Surface, Foreground);
        exportJson.Click += delegate { ExportCaptures(false); };
        exportCsv.Click += delegate { ExportCaptures(true); };
        buttons.Controls.Add(captureBefore, 0, 0); buttons.Controls.Add(captureAfter, 1, 0); buttons.Controls.Add(exportJson, 2, 0); buttons.Controls.Add(exportCsv, 3, 0);
        layout.Controls.Add(buttons, 0, 1);
        TableLayoutPanel captureState = Table(1, 2);
        captureState.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        captureState.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
        captureStatus.Text = "No captures yet. — means a metric is unavailable or not captured.";
        captureStatus.Dock = DockStyle.Fill; captureStatus.ForeColor = Muted; captureStatus.Font = new Font("Segoe UI", 9);
        captureStatus.TextAlign = ContentAlignment.MiddleLeft;
        captureState.Controls.Add(captureStatus, 0, 0);
        captureProgress.Dock = DockStyle.Fill; captureProgress.Minimum = 0; captureProgress.Maximum = 100;
        captureState.Controls.Add(captureProgress, 0, 1);
        layout.Controls.Add(captureState, 0, 2);
        RoundedCard gridCard = new RoundedCard { Padding = new Padding(12), Margin = new Padding(0, 0, 0, 8) };
        StyleGrid(comparisons);
        comparisons.Columns.Add("metric", "Metric"); comparisons.Columns.Add("before", "Before avg"); comparisons.Columns.Add("after", "After avg"); comparisons.Columns.Add("change", "After − Before");
        comparisons.Columns[0].FillWeight = 155;
        gridCard.Controls.Add(comparisons);
        layout.Controls.Add(gridCard, 0, 3);
        TableLayoutPanel workloadRow = Table(2, 1);
        workloadRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        workloadRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workloadRow.Controls.Add(LabelText("WORKLOAD NOTE", 8.5F, Muted, FontStyle.Bold), 0, 0);
        StyleInput(workload); workload.Dock = DockStyle.Top; workload.Margin = new Padding(0, 9, 0, 0);
        workloadRow.Controls.Add(workload, 1, 0);
        layout.Controls.Add(workloadRow, 0, 4);
        RefreshComparison();
        return page;
    }

    private void SelectPage(int index)
    {
        string[] names = { "System at a glance", "Live monitor", "Before & after" };
        title.Text = names[index];
        for (int page = 0; page < 3; page++)
        {
            pages[page].Visible = page == index;
            navigation[page].BackColor = page == index ? Color.FromArgb(30, 62, 78) : Sidebar;
            navigation[page].ForeColor = page == index ? Cyan : Muted;
            navigation[page].Invalidate();
        }
        pages[index].BringToFront();
    }

    private void RequestSample()
    {
        if (sampling || IsDisposed) return;
        sampling = true;
        ThreadPool.QueueUserWorkItem(delegate {
            TelemetrySample sample = null;
            Exception failure = null;
            try { sample = telemetry.Sample(); } catch (Exception error) { failure = error; }
            Post(delegate {
                sampling = false;
                if (failure != null) { sensorStatus.Text = "Sensors: some readings unavailable  ·  " + failure.Message; return; }
                UpdateTelemetry(sample);
            });
        });
    }

    private void UpdateTelemetry(TelemetrySample sample)
    {
        history.Add(sample);
        if (history.Count > 120) history.RemoveAt(0);
        overviewCpu.Set(Format(sample.CpuPercent, "0", "%"), "Whole-system processor activity", sample.CpuPercent);
        overviewRam.Set(Format(sample.MemoryUsedGB, "0.0", " GiB"), "of " + Format(sample.MemoryTotalGB, "0.0", " GiB") + " physical RAM", sample.MemoryPercent);
        GpuReading gpu = sample.Gpu;
        double? gpuUse = gpu == null ? null : gpu.UtilizationPercent;
        string gpuName = gpu == null || String.IsNullOrWhiteSpace(gpu.Name) ? "NVIDIA telemetry when available" : gpu.Name;
        overviewGpu.Set(Format(gpuUse, "0", "%"), gpuName, gpuUse);
        liveCpu.Set(Format(sample.CpuPercent, "0.0", "%"), "Whole-system activity", sample.CpuPercent);
        liveRam.Set(Format(sample.MemoryUsedGB, "0.0", " GiB"), Format(sample.MemoryPercent, "0.0", "%") + " of physical memory", sample.MemoryPercent);
        liveGpu.Set(Format(gpuUse, "0.0", "%"), gpu == null ? "Unavailable" : Format(gpu.MemoryUsedMB, "0", " MiB VRAM") + (gpu.IsStale ? " · cached" : ""), gpuUse);
        liveDisk.Set(Format(sample.DiskReadMBps, "0.0", "") + " / " + Format(sample.DiskWriteMBps, "0.0", ""), "MiB/s  ·  " + Format(sample.DiskActivePercent, "0.0", "% active"), sample.DiskActivePercent);
        liveNetwork.Set(Format(sample.NetworkReceiveMBps, "0.00", "") + " / " + Format(sample.NetworkSendMBps, "0.00", ""), "MiB/s across active interfaces", null);
        liveGpuPower.Set(gpu == null ? "—" : Format(gpu.TemperatureC, "0", "°C") + " / " + Format(gpu.PowerWatts, "0", " W"), gpu == null ? "Unavailable" : gpuName, null);
        chart.SetSamples(history);
        drives.Rows.Clear();
        if (sample.Storage != null)
            foreach (StorageReading storage in sample.Storage)
                drives.Rows.Add(storage.Name, Format(storage.FreeGB, "0.0", ""), Format(storage.TotalGB, "0.0", ""), Format(storage.UsedPercent, "0", "%"));
        driveStatus.Text = drives.Rows.Count == 0 ? "STORAGE CAPACITY  ·  waiting" : "STORAGE CAPACITY  ·  GiB, binary units";
        StringBuilder readingNotes = new StringBuilder("READING STATUS\n");
        if (gpu != null && !String.IsNullOrWhiteSpace(gpu.Status)) readingNotes.Append(gpu.Status).Append('\n');
        if (sample.Notes != null)
            foreach (string note in sample.Notes) { if (readingNotes.Length > 330) break; readingNotes.Append(note).Append('\n'); }
        if (readingNotes.Length <= 16) readingNotes.Append("Live data available. — means unavailable.");
        notes.Text = readingNotes.ToString();
        sensorStatus.Text = "Sensors: live at " + DateTime.Now.ToString("HH:mm:ss") + "  ·  CPU/RAM every second; GPU/storage every five seconds  ·  — means unavailable";
        if (capturing)
        {
            captureSamples.Add(sample);
            double seconds = (DateTime.UtcNow - captureStart).TotalSeconds;
            captureProgress.Value = Math.Min(100, (int)(seconds * 10));
            captureStatus.Text = "Capturing " + (captureIsBefore ? "Before" : "After") + "  ·  " + Math.Min(10, (int)seconds) + " / 10 seconds  ·  keep the same workload running";
            if (seconds >= 10) FinishCapture();
        }
    }

    private void LaunchChosenProfile()
    {
        if (preset.SelectedIndex == 0 && String.IsNullOrWhiteSpace(config.Text))
        {
            MessageBox.Show(this, "Choose a preset or configuration first, or use Open full WinUtil to choose individual options.", "No profile selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try { OpenVendor(Launcher.ParseOptions(ChosenProfileArguments())); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Profile could not start", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private string[] ChosenProfileArguments()
    {
        List<string> arguments = new List<string>();
        string selectedPreset = PresetArgument(preset.SelectedIndex);
        // Gaming is an explicit single-tweak companion profile. A disabled, previously
        // entered config must not silently add changes to it.
        if (selectedPreset != "Gaming" && !String.IsNullOrWhiteSpace(config.Text)) { arguments.Add("-Config"); arguments.Add(config.Text.Trim()); }
        if (selectedPreset != null) { arguments.Add("-Preset"); arguments.Add(selectedPreset); }
        if (offline.Checked) arguments.Add("-Offline");
        return arguments.ToArray();
    }

    private void OpenVendor(Launcher.LaunchOptions options)
    {
        if (previewMode || operationActive) return;
        fullWinUtil.Enabled = applyProfile.Enabled = false;
        if (logWindow != null && !logWindow.IsDisposed) { logWindow.Close(); logWindow = null; }
        if (vendorSession != null) { vendorSession.Dispose(); vendorSession = null; }
        openLog.Enabled = false;
        SetOperationStatus("Starting", "Waiting for Windows administrator approval. Monitoring continues.", true, 0);
        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                VendorSession session = Launcher.StartVendorSession(options);
                Post(delegate { vendorSession = session; openLog.Enabled = true; SetOperationStatus("Running", options.Preset == "Gaming" ? "Enabling Windows Game Mode. Show log for details." : "WinUtil is running. Show log for progress and errors.", true, 0); PollVendorSession(); });
            }
            catch (Exception error)
            {
                Post(delegate {
                    fullWinUtil.Enabled = true;
                    VendorSession failedSession = error.Data["WinUtilSession"] as VendorSession;
                    if (failedSession != null) { vendorSession = failedSession; openLog.Enabled = true; }
                    Win32Exception windowsError = error as Win32Exception;
                    if (windowsError != null && windowsError.NativeErrorCode == 1223) { SetOperationStatus("Canceled", "Administrator approval was canceled. No WinUtil action started.", false, 0); RefreshProfileSummary(); return; }
                    SetOperationStatus("Failed", "WinUtil could not start: " + error.Message, false, 1);
                    RefreshProfileSummary();
                    MessageBox.Show(this, error.Message, "WinUtil could not start", MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            }
        });
    }

    private void PollVendorSession()
    {
        if (vendorSession == null || !operationActive || sessionPolling || IsDisposed) return;
        VendorSession session = vendorSession;
        sessionPolling = true;
        ThreadPool.QueueUserWorkItem(delegate {
            VendorSessionSnapshot snapshot = null; Exception failure = null;
            try { snapshot = session.RefreshSnapshot(); } catch (Exception error) { failure = error; }
            Post(delegate {
                sessionPolling = false;
                if (!Object.ReferenceEquals(session, vendorSession)) return;
                if (failure != null) { SetOperationStatus(operationStage, "Status temporarily unavailable. Show log for details.", operationActive, operationErrors); return; }
                if (snapshot == null) return;
                string message = String.IsNullOrWhiteSpace(snapshot.Message) ? "Show log for progress and error details." : snapshot.Message;
                if (snapshot.ErrorCount > 0 && !String.IsNullOrWhiteSpace(snapshot.LastError)) message += "  ·  " + snapshot.LastError;
                SetOperationStatus(String.IsNullOrWhiteSpace(snapshot.Stage) ? "Running" : snapshot.Stage, message, !snapshot.Completed, snapshot.ErrorCount);
                fullWinUtil.Enabled = snapshot.Completed;
                RefreshProfileSummary();
            });
        });
    }

    private void ShowRunLog()
    {
        if (vendorSession == null) return;
        if (logWindow == null || logWindow.IsDisposed)
        {
            VendorSession session = vendorSession;
            logWindow = new RunLogWindow(session.LogPath, delegate { return session.ReadLogTail(250000); });
            logWindow.Show(this);
        }
        else logWindow.Activate();
        logWindow.RefreshLog();
    }

    private void BeginCapture(bool isBefore)
    {
        if (previewMode || capturing) return;
        captureIsBefore = isBefore;
        captureStart = DateTime.UtcNow;
        captureSamples = new List<TelemetrySample>();
        capturing = true;
        captureBefore.Enabled = captureAfter.Enabled = false;
        captureProgress.Value = 0;
        captureStatus.Text = "Capturing " + (isBefore ? "Before" : "After") + " for ten seconds. Keep your workload unchanged.";
        SetActivityStatus("Capturing " + (isBefore ? "Before" : "After") + " for ten seconds. Monitoring continues.");
        RequestSample();
    }

    private void FinishCapture()
    {
        CaptureSnapshot snapshot = CaptureSnapshot.FromSamples(captureIsBefore ? "Before" : "After", captureStart, captureSamples, workload.Text);
        if (captureIsBefore) before = snapshot; else after = snapshot;
        capturing = false;
        captureBefore.Enabled = captureAfter.Enabled = true;
        captureProgress.Value = 100;
        captureStatus.Text = snapshot.Name + " captured  ·  " + snapshot.SampleCount + " samples over " + snapshot.DurationSeconds.ToString("0.0") + " seconds. Compare the same workload.";
        SetActivityStatus(snapshot.Name + " capture complete. Compare the same workload.");
        RefreshComparison();
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinUtilPerformance", "Captures");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, snapshot.Name.ToLowerInvariant() + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(snapshot), Encoding.UTF8);
            File.Move(temporary, path);
        }
        catch (Exception error) { captureStatus.Text = snapshot.Name + " remains available here; local save failed: " + error.Message; }
    }

    private static readonly string[] MetricNames = { "CPU activity", "RAM used", "Disk read", "Disk write", "Disk active time", "Network receive", "Network send", "GPU activity", "GPU VRAM used", "GPU temperature", "GPU power", "Storage free (shown drives)" };
    private static readonly string[] MetricUnits = { "%", "GiB", "MiB/s", "MiB/s", "%", "MiB/s", "MiB/s", "%", "MiB", "°C", "W", "GiB" };

    private static double?[] ComparisonValues(CaptureSnapshot capture)
    {
        double?[] values = new double?[MetricNames.Length];
        if (capture == null) return values;
        Array.Copy(capture.Average.Values(), values, 11);
        if (capture.Storage != null)
        {
            double free = 0; int valid = 0;
            foreach (StorageReading drive in capture.Storage) if (drive.FreeGB.HasValue) { free += drive.FreeGB.Value; valid++; }
            if (valid != 0) values[11] = free;
        }
        return values;
    }

    private static bool SameStorageVolumes(CaptureSnapshot first, CaptureSnapshot second)
    {
        if (first == null || second == null || first.Storage == null || second.Storage == null) return false;
        HashSet<string> beforeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> afterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (StorageReading drive in first.Storage) if (drive.FreeGB.HasValue && !String.IsNullOrWhiteSpace(drive.Name)) beforeNames.Add(drive.Name);
        foreach (StorageReading drive in second.Storage) if (drive.FreeGB.HasValue && !String.IsNullOrWhiteSpace(drive.Name)) afterNames.Add(drive.Name);
        return beforeNames.Count != 0 && beforeNames.SetEquals(afterNames);
    }

    private void RefreshComparison()
    {
        comparisons.Rows.Clear();
        double?[] beforeValues = ComparisonValues(before);
        double?[] afterValues = ComparisonValues(after);
        for (int index = 0; index < MetricNames.Length; index++)
        {
            bool comparable = index != 11 || SameStorageVolumes(before, after);
            double? change = comparable && beforeValues[index].HasValue && afterValues[index].HasValue ? (double?)(afterValues[index].Value - beforeValues[index].Value) : null;
            string changeUnit = MetricUnits[index] == "%" ? " pp" : " " + MetricUnits[index];
            int row = comparisons.Rows.Add(MetricNames[index], Format(beforeValues[index], "0.00", " " + MetricUnits[index]), Format(afterValues[index], "0.00", " " + MetricUnits[index]), Format(change, "+0.00;-0.00;0.00", changeUnit));
            if (change.HasValue) comparisons.Rows[row].Cells[3].Style.ForeColor = Cyan;
        }
    }

    private void ExportCaptures(bool csv)
    {
        if (before == null && after == null) { MessageBox.Show(this, "Capture Before or After first.", "Nothing to export", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using (SaveFileDialog dialog = new SaveFileDialog { Title = "Export performance comparison", Filter = csv ? "CSV (*.csv)|*.csv" : "JSON (*.json)|*.json", FileName = "WinUtil-comparison." + (csv ? "csv" : "json") })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                string content;
                if (!csv) content = new JavaScriptSerializer().Serialize(new {
                    Application = "WinUtil Performance Dashboard", WinUtilVersion = "26.10.07", ExportedUtc = DateTime.UtcNow.ToString("o"),
                    Interpretation = "Observed activity averages, not benchmark scores. Use the same workload. Percent changes are percentage-point differences.", Before = before, After = after
                });
                else
                {
                    StringBuilder data = new StringBuilder("Metric,Unit,BeforeAverage,AfterAverage,AfterMinusBefore,ChangeUnit\r\n");
                    double?[] first = ComparisonValues(before);
                    double?[] second = ComparisonValues(after);
                    for (int index = 0; index < MetricNames.Length; index++)
                    {
                        bool comparable = index != 11 || SameStorageVolumes(before, after);
                        double? change = comparable && first[index].HasValue && second[index].HasValue ? (double?)(second[index].Value - first[index].Value) : null;
                        data.Append(MetricNames[index]).Append(',').Append(MetricUnits[index]).Append(',').Append(CsvNumber(first[index])).Append(',').Append(CsvNumber(second[index])).Append(',').Append(CsvNumber(change)).Append(',').Append(MetricUnits[index] == "%" ? "percentage points" : MetricUnits[index]).Append("\r\n");
                    }
                    content = data.ToString();
                }
                File.WriteAllText(dialog.FileName, content, Encoding.UTF8);
                SetActivityStatus("Exported comparison to " + dialog.FileName);
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

    private static string CsvNumber(double? value) { return value.HasValue ? value.Value.ToString("0.######", CultureInfo.InvariantCulture) : ""; }

    private void LoadSavedCaptures()
    {
        try
        {
            before = LoadSavedCapture("Before");
            after = LoadSavedCapture("After");
            if (before != null || after != null)
            {
                RefreshComparison();
                captureStatus.Text = "Loaded your saved captures. Compare the same workload; percentage changes use percentage points.";
            }
        }
        catch (Exception error) { captureStatus.Text = "Saved captures could not be loaded: " + error.Message; }
    }

    private static CaptureSnapshot LoadSavedCapture(string phase)
    {
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinUtilPerformance", "Captures");
        if (Directory.Exists(cache))
        {
            string[] saved = Directory.GetFiles(cache, phase.ToLowerInvariant() + "-*.json");
            Array.Sort(saved, StringComparer.OrdinalIgnoreCase);
            if (saved.Length != 0) return new JavaScriptSerializer().Deserialize<CaptureSnapshot>(File.ReadAllText(saved[saved.Length - 1]));
        }
        return null;
    }
    private void Post(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { }
    }

    internal Bitmap RenderPreview(int page = 0)
    {
        // Render only the initial controls, without personal captures or hardware readings.
        // This is not a screen/desktop capture and does not execute WinUtil.
        SetOperationStatus("Ready", "Choose a profile or open full WinUtil. No automatic changes selected.", false, 0);
        sensorStatus.Text = "PREVIEW  ·  Initial state; no personal readings or captures; no WinUtil action executed";
        SelectPage(page);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        ShowInTaskbar = false;
        Show();
        Application.DoEvents();
        IntPtr windowHandle = Handle;
        ForceHandles(this);
        PerformLayout();
        Bitmap image = new Bitmap(Width, Height);
        DrawToBitmap(image, new Rectangle(0, 0, Width, Height));
        Hide();
        return image;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int dark = 1;
            DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            int caption = Background.R | Background.G << 8 | Background.B << 16;
            int text = Foreground.R | Foreground.G << 8 | Foreground.B << 16;
            DwmSetWindowAttribute(Handle, 35, ref caption, 4);
            DwmSetWindowAttribute(Handle, 36, ref text, 4);
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private static void ForceHandles(Control control)
    {
        IntPtr handle = control.Handle;
        foreach (Control child in control.Controls) ForceHandles(child);
        control.PerformLayout();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { if (logWindow != null) logWindow.Dispose(); if (timer != null) timer.Dispose(); if (telemetry != null) telemetry.Dispose(); }
        base.Dispose(disposing);
    }

    internal static Dictionary<string, object> RunUiSelfTests()
    {
        Dictionary<string, object> results = new Dictionary<string, object>();
        using (Dashboard dashboard = new Dashboard(true))
        {
            dashboard.config.Text = "https://example.invalid/profile.json";
            dashboard.preset.SelectedIndex = 1;
            string[] gaming = dashboard.ChosenProfileArguments();
            RequireUi(gaming.Length == 2 && gaming[0] == "-Preset" && gaming[1] == "Gaming", "Gaming must launch the single companion profile without a leftover config.");
            RequireUi(!dashboard.config.Enabled && dashboard.profileSummary.Text.Contains("WPFToggleGameMode"), "Gaming needs an exact scope summary and disabled config.");
            RequireUi(Launcher.ParseOptions(gaming).Preset == "Gaming", "Gaming must be accepted by the launcher.");
            results["gamingPresetMapsWithoutExtraConfig"] = true;
            results["gamingScopeVisible"] = true;
            dashboard.config.Text = "";
            string[] vendorPresets = { "Standard", "Minimal", "Advanced" };
            for (int index = 0; index < vendorPresets.Length; index++)
            {
                dashboard.preset.SelectedIndex = index + 2;
                string[] selected = dashboard.ChosenProfileArguments();
                RequireUi(selected.Length == 2 && selected[1] == vendorPresets[index], "Vendor preset mapping changed.");
            }
            results["originalPresetsPreserved"] = true;
            dashboard.preset.SelectedIndex = 0;
            RequireUi(!dashboard.applyProfile.Enabled && !dashboard.openLog.Enabled, "Apply and log controls must be unavailable before a selection or run.");
            dashboard.ShowRunLog();
            RequireUi(dashboard.logWindow == null, "Show log without a run must not open a dialog or launch anything.");
            results["noRunLogIsNonMutating"] = true;
            dashboard.SetOperationStatus("Running", "Applying a synthetic test profile", true, 2);
            string operation = dashboard.status.Text;
            TelemetrySample synthetic = new TelemetrySample { Timestamp = DateTime.UtcNow.ToString("o"), CpuPercent = 15, MemoryUsedGB = 8, MemoryTotalGB = 16, MemoryPercent = 50, Storage = new List<StorageReading>(), Notes = new List<string>() };
            dashboard.UpdateTelemetry(synthetic);
            dashboard.SetActivityStatus("A comparison export finished");
            RequireUi(dashboard.status.Text == operation && dashboard.errorsStatus.Text == "Errors: 2" && dashboard.operationProgress.IsIndeterminate, "Telemetry or export replaced active operation status.");
            RequireUi(dashboard.sensorStatus.Text.Contains("Sensors: live"), "Sensor status must update separately.");
            results["operationSurvivesTelemetryAndExport"] = true;
            results["separateSensorStatusAndErrorCount"] = true;
            dashboard.SetOperationStatus("Completed", "Synthetic run finished; inspect log", false, 2);
            operation = dashboard.status.Text;
            dashboard.UpdateTelemetry(synthetic);
            RequireUi(dashboard.status.Text == operation && !dashboard.operationProgress.IsIndeterminate, "Completed status must persist after later readings.");
            results["completionSurvivesTelemetry"] = true;
            dashboard.Size = dashboard.MinimumSize;
            ForceHandles(dashboard);
            dashboard.PerformLayout();
            Rectangle statusBounds = dashboard.RectangleToClient(dashboard.status.RectangleToScreen(dashboard.status.ClientRectangle));
            RequireUi(statusBounds.Top >= 0 && statusBounds.Bottom <= dashboard.ClientSize.Height && statusBounds.Width > 250, "Status bar was clipped at the minimum window size.");
            RequireUi(dashboard.pages[0].AutoScroll && dashboard.pages[1].AutoScroll && dashboard.pages[2].AutoScroll, "Short windows need scroll access to page content.");
            results["minimumWindowKeepsStatusVisibleAndPagesScrollable"] = true;
        }
        string testPath = Path.Combine(Path.GetTempPath(), "winutil-log-ui-test-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            const string expected = "SYNTHETIC UI TEST\r\nERROR: Example diagnostic details\r\n";
            File.WriteAllText(testPath, expected, Encoding.UTF8);
            using (RunLogWindow window = new RunLogWindow(testPath, delegate { return File.ReadAllText(testPath); }))
            {
                window.LoadForTest();
                RequireUi(window.DisplayedLog == expected && window.DisplayedPath == testPath, "Run log viewer must show the actual selected run's readable log and path.");
            }
            results["logViewerReadsActualSyntheticFile"] = true;
        }
        finally { if (File.Exists(testPath)) File.Delete(testPath); }
        results["success"] = true;
        results["vendorScriptExecuted"] = false;
        results["elevationRequested"] = false;
        results["personalDataRead"] = false;
        return results;
    }

    private static void RequireUi(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Dashboard UI check failed: " + message);
    }

    private static TableLayoutPanel Table(int columns, int rows)
    {
        TableLayoutPanel layout = new TableLayoutPanel { ColumnCount = columns, RowCount = rows, Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0), BackColor = Color.Transparent };
        if (columns == 1) layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return layout;
    }
    private static void MakeScrollable(Panel page, TableLayoutPanel layout, int minimumHeight)
    {
        // A Dock.Fill child is excluded from WinForms' automatic scroll extent.
        // Dock.Top plus a minimum content height keeps every action reachable.
        layout.Dock = DockStyle.Top;
        layout.Height = minimumHeight;
        page.AutoScrollMinSize = new Size(0, minimumHeight);
        page.Resize += delegate { layout.Height = Math.Max(minimumHeight, page.ClientSize.Height); };
    }
    private static Label LabelText(string text, float size, Color color, FontStyle style = FontStyle.Regular)
    {
        return new Label { Text = text, ForeColor = color, Font = new Font("Segoe UI", size, style), Dock = DockStyle.Fill, AutoSize = false, Margin = new Padding(0), BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
    }
    private static RoundedButton Button(string text, Color background, Color foreground)
    {
        return new RoundedButton { Text = text, BackColor = background, ForeColor = foreground, Font = new Font("Segoe UI", 10, FontStyle.Bold), Dock = DockStyle.Fill, Margin = new Padding(4, 3, 4, 5) };
    }
    private static void StyleInput(TextBox input)
    {
        input.BackColor = Sidebar; input.ForeColor = Foreground; input.BorderStyle = BorderStyle.FixedSingle; input.Font = new Font("Segoe UI", 10);
    }
    private static void StyleGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill; grid.BackgroundColor = Surface; grid.BorderStyle = BorderStyle.None; grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = Color.FromArgb(37, 53, 74); grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = Muted, Font = new Font("Segoe UI", 9, FontStyle.Bold), SelectionBackColor = Surface, SelectionForeColor = Muted };
        grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = Foreground, Font = new Font("Segoe UI", 9), SelectionBackColor = Color.FromArgb(35, 59, 80), SelectionForeColor = Foreground, Padding = new Padding(3) };
        grid.RowHeadersVisible = false; grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.RowTemplate.Height = 27; grid.ColumnHeadersHeight = 28;
        grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false; grid.ReadOnly = true; grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
    }
    internal static string Format(double? number, string format, string suffix)
    {
        return number.HasValue && !Double.IsNaN(number.Value) && !Double.IsInfinity(number.Value) ? number.Value.ToString(format, CultureInfo.InvariantCulture) + suffix : "—";
    }
}

internal class RoundedCard : Panel
{
    internal RoundedCard() { Dock = DockStyle.Fill; BackColor = Color.Transparent; Padding = new Padding(17); DoubleBuffered = true; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 13))
        using (SolidBrush fill = new SolidBrush(Dashboard.Surface))
        using (Pen outline = new Pen(Color.FromArgb(36, 52, 74))) { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(outline, path); }
    }
    internal static GraphicsPath RoundedPath(Rectangle rectangle, int radius)
    {
        GraphicsPath path = new GraphicsPath();
        int size = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
        if (size <= 0) { path.AddRectangle(rectangle); return path; }
        path.AddArc(rectangle.Left, rectangle.Top, size, size, 180, 90);
        path.AddArc(rectangle.Right - size, rectangle.Top, size, size, 270, 90);
        path.AddArc(rectangle.Right - size, rectangle.Bottom - size, size, size, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - size, size, size, 90, 90); path.CloseFigure(); return path;
    }
}

internal sealed class RoundedButton : Button
{
    private bool hover;
    internal RoundedButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; DoubleBuffered = true; }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color fill = !Enabled ? Color.FromArgb(37, 49, 67) : (hover ? ControlPaint.Light(BackColor, 0.08F) : BackColor);
        using (GraphicsPath path = RoundedCard.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 8))
        using (Brush brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
        TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        flags |= TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(12, 0, Width - 24, Height), Enabled ? ForeColor : Dashboard.Muted, flags);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(5, 5, Width - 10, Height - 10), ForeColor, fill);
    }
}

internal sealed class ThinProgress : Control
{
    public int Minimum = 0, Maximum = 100;
    private int value;
    private int animationStep;
    internal bool IsIndeterminate;
    public int Value { get { return value; } set { this.value = Math.Min(Maximum, Math.Max(Minimum, value)); Invalidate(); } }
    internal ThinProgress() { DoubleBuffered = true; }
    internal void AdvanceAnimation() { if (IsIndeterminate) { animationStep = (animationStep + 1) % 12; Invalidate(); } }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.FromArgb(28, 45, 64));
        if (IsIndeterminate)
        {
            int segmentWidth = Math.Max(30, Width / 5);
            int left = (Width + segmentWidth) * animationStep / 11 - segmentWidth;
            using (Brush accent = new SolidBrush(Dashboard.Cyan)) e.Graphics.FillRectangle(accent, left, 0, segmentWidth, Height);
            return;
        }
        if (value > Minimum)
            using (Brush accent = new SolidBrush(Dashboard.Cyan)) e.Graphics.FillRectangle(accent, 0, 0, Width * (value - Minimum) / Math.Max(1, Maximum - Minimum), Height);
    }
}

internal sealed class RunLogWindow : Form
{
    private readonly Func<string> readLog;
    private readonly TextBox log = new TextBox();
    private readonly Label path = new Label();
    private readonly Label readingStatus = new Label();
    private bool reading;
    internal string DisplayedLog { get { return log.Text; } }
    internal string DisplayedPath { get { return path.Text; } }

    internal RunLogWindow(string logPath, Func<string> reader)
    {
        readLog = reader;
        Text = "WinUtil run log"; BackColor = Dashboard.Background; ForeColor = Dashboard.Foreground;
        Font = new Font("Segoe UI", 10); ClientSize = new Size(820, 500); MinimumSize = new Size(570, 350);
        StartPosition = FormStartPosition.CenterParent;
        TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 4, BackColor = Dashboard.Background };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 33)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(new Label { Text = "Run output & error details", Dock = DockStyle.Fill, ForeColor = Dashboard.Foreground, Font = new Font("Segoe UI", 16, FontStyle.Bold), AutoSize = false }, 0, 0);
        path.Text = logPath; path.Dock = DockStyle.Fill; path.ForeColor = Dashboard.Muted; path.Font = new Font("Segoe UI", 8.5F); path.AutoEllipsis = true; path.UseMnemonic = false;
        layout.Controls.Add(path, 0, 1);
        log.Dock = DockStyle.Fill; log.Multiline = true; log.ReadOnly = true; log.WordWrap = false; log.ScrollBars = ScrollBars.Both;
        log.BackColor = Dashboard.Sidebar; log.ForeColor = Dashboard.Foreground; log.BorderStyle = BorderStyle.FixedSingle;
        log.Font = new Font("Consolas", 9.5F); log.Text = "Waiting for run output…";
        layout.Controls.Add(log, 0, 2);
        readingStatus.Text = "Updates while the run is active. This window shows the latest log tail.";
        readingStatus.ForeColor = Dashboard.Muted; readingStatus.Dock = DockStyle.Fill; readingStatus.Font = new Font("Segoe UI", 8.5F); readingStatus.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(readingStatus, 0, 3); Controls.Add(layout);
        Shown += delegate { RefreshLog(); };
    }

    internal void RefreshLog()
    {
        if (reading || IsDisposed || !IsHandleCreated) return;
        reading = true;
        ThreadPool.QueueUserWorkItem(delegate {
            string text = null; Exception failure = null;
            try { text = readLog(); } catch (Exception error) { failure = error; }
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action(delegate {
                reading = false;
                if (failure != null) { readingStatus.Text = "Log temporarily unavailable: " + failure.Message; return; }
                SetLog(text);
            })); } catch (InvalidOperationException) { }
        });
    }

    internal void LoadForTest() { SetLog(readLog()); }

    private void SetLog(string text)
    {
        string readable = String.IsNullOrWhiteSpace(text) ? "Waiting for run output…" : text;
        if (log.Text != readable)
        {
            bool followTail = log.SelectionStart >= Math.Max(0, log.TextLength - 2);
            int selection = log.SelectionStart;
            log.Text = readable;
            log.SelectionStart = followTail ? log.TextLength : Math.Min(selection, log.TextLength);
            log.SelectionLength = 0;
            if (followTail) log.ScrollToCaret();
        }
        readingStatus.Text = "Updated " + DateTime.Now.ToString("HH:mm:ss") + "  ·  Latest run output and persisted error details";
    }
}

internal sealed class MetricTile : RoundedCard
{
    private readonly Label value = new Label();
    private readonly Label detail = new Label();
    private readonly Color accent;
    private double? percent;
    internal MetricTile(string heading, Color color, bool compact = false)
    {
        accent = color;
        Margin = new Padding(0, 0, 9, 10); Padding = new Padding(16, 12, 13, 15);
        TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0), BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 21));
        layout.Controls.Add(new Label { Text = heading, Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = Dashboard.Muted, Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0) }, 0, 0);
        value.Text = "—"; value.Font = new Font("Segoe UI", compact ? 18 : 21, FontStyle.Bold); value.ForeColor = color; value.Dock = DockStyle.Fill; value.BackColor = Color.Transparent; value.Margin = new Padding(0); value.TextAlign = ContentAlignment.MiddleLeft;
        detail.Text = "Waiting for readings"; detail.Font = new Font("Segoe UI", 8.5F); detail.ForeColor = Dashboard.Muted; detail.Dock = DockStyle.Fill; detail.BackColor = Color.Transparent; detail.Margin = new Padding(0); detail.AutoEllipsis = true;
        layout.Controls.Add(value, 0, 1); layout.Controls.Add(detail, 0, 2); Controls.Add(layout);
    }
    internal void Set(string number, string description, double? usage) { value.Text = number; detail.Text = description; percent = usage; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (percent.HasValue)
        {
            Rectangle track = new Rectangle(16, Height - 9, Width - 32, 3);
            using (Brush background = new SolidBrush(Color.FromArgb(43, 61, 82))) e.Graphics.FillRectangle(background, track);
            using (Brush color = new SolidBrush(accent)) e.Graphics.FillRectangle(color, new Rectangle(track.X, track.Y, (int)(track.Width * Math.Min(100, Math.Max(0, percent.Value)) / 100), track.Height));
        }
    }
}

internal sealed class UtilizationChart : Control
{
    private List<TelemetrySample> samples = new List<TelemetrySample>();
    internal UtilizationChart() { DoubleBuffered = true; BackColor = Dashboard.Surface; }
    internal void SetSamples(List<TelemetrySample> readings) { samples = new List<TelemetrySample>(readings); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath path = RoundedCard.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 12))
        using (Brush background = new SolidBrush(Dashboard.Surface)) e.Graphics.FillPath(background, path);
        TextRenderer.DrawText(e.Graphics, "ACTIVITY HISTORY  ·  Last 120 samples", new Font("Segoe UI", 9, FontStyle.Bold), new Point(17, 12), Dashboard.Muted);
        TextRenderer.DrawText(e.Graphics, "CPU", Font, new Point(Width - 191, 12), Dashboard.Cyan);
        TextRenderer.DrawText(e.Graphics, "RAM", Font, new Point(Width - 135, 12), Dashboard.Violet);
        TextRenderer.DrawText(e.Graphics, "GPU", Font, new Point(Width - 76, 12), Dashboard.Mint);
        Rectangle graph = new Rectangle(43, 43, Math.Max(1, Width - 62), Math.Max(1, Height - 65));
        for (int level = 0; level <= 4; level++)
        {
            int y = graph.Bottom - graph.Height * level / 4;
            using (Pen grid = new Pen(Color.FromArgb(37, 53, 74))) e.Graphics.DrawLine(grid, graph.Left, y, graph.Right, y);
            TextRenderer.DrawText(e.Graphics, (level * 25).ToString(), new Font("Segoe UI", 7), new Point(10, y - 7), Dashboard.Muted);
        }
        DrawSeries(e.Graphics, graph, delegate(TelemetrySample sample) { return sample.CpuPercent; }, Dashboard.Cyan);
        DrawSeries(e.Graphics, graph, delegate(TelemetrySample sample) { return sample.MemoryPercent; }, Dashboard.Violet);
        DrawSeries(e.Graphics, graph, delegate(TelemetrySample sample) { return sample.Gpu == null ? null : sample.Gpu.UtilizationPercent; }, Dashboard.Mint);
        if (samples.Count < 2) TextRenderer.DrawText(e.Graphics, "History builds while the dashboard is open", Font, graph, Dashboard.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
    private void DrawSeries(Graphics graphics, Rectangle graph, Func<TelemetrySample, double?> selector, Color color)
    {
        PointF? previous = null;
        using (Pen line = new Pen(color, 2))
            for (int index = 0; index < samples.Count; index++)
            {
                double? value = selector(samples[index]);
                if (!value.HasValue) { previous = null; continue; }
                PointF point = new PointF(graph.Right - (samples.Count - 1 - index) * graph.Width / 119F, graph.Bottom - (float)Math.Max(0, Math.Min(100, value.Value)) * graph.Height / 100F);
                if (previous.HasValue) graphics.DrawLine(line, previous.Value, point); previous = point;
            }
    }
}

public sealed class CaptureSnapshot
{
    public string Name, StartedUtc, FinishedUtc, WorkloadNote;
    public string Caveat;
    public double DurationSeconds;
    public int SampleCount;
    public AverageMetrics Average;
    public List<TelemetrySample> Samples;
    public List<StorageReading> Storage;
    internal static CaptureSnapshot FromSamples(string name, DateTime start, List<TelemetrySample> samples, string note)
    {
        return new CaptureSnapshot { Name = name, StartedUtc = start.ToString("o"), FinishedUtc = DateTime.UtcNow.ToString("o"), DurationSeconds = (DateTime.UtcNow - start).TotalSeconds, SampleCount = samples.Count, WorkloadNote = note, Samples = samples, Average = AverageMetrics.FromSamples(samples), Storage = samples.Count == 0 ? null : samples[samples.Count - 1].Storage, Caveat = "Observed activity. Use the same workload; a lower activity percentage is not proof of higher performance." };
    }
    internal static CaptureSnapshot FromObservation(string json, string name)
    {
        Dictionary<string, object> data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
        if (!data.ContainsKey("Summary")) throw new InvalidDataException("Observation has no Summary.");
        Dictionary<string, object> summary = data["Summary"] as Dictionary<string, object>;
        if (summary == null) throw new InvalidDataException("Observation Summary is not an object.");
        AverageMetrics average = new AverageMetrics {
            CpuPercent = ReadNumber(summary, "CpuPercent"), MemoryUsedGB = ReadNumber(summary, "MemoryUsedGB"),
            DiskReadMBps = ReadNumber(summary, "DiskReadMBps"), DiskWriteMBps = ReadNumber(summary, "DiskWriteMBps"),
            DiskActivePercent = ReadNumber(summary, "DiskActivePercent"), NetworkReceiveMBps = ReadNumber(summary, "NetworkReceiveMBps"), NetworkSendMBps = ReadNumber(summary, "NetworkSendMBps")
        };
        List<StorageReading> storage = new List<StorageReading>();
        object rawStorage;
        if (data.TryGetValue("Storage", out rawStorage))
        {
            System.Collections.IEnumerable rows = rawStorage as System.Collections.IEnumerable;
            if (rows != null) foreach (object item in rows)
            {
                Dictionary<string, object> row = item as Dictionary<string, object>;
                if (row == null) continue;
                object driveName; row.TryGetValue("Drive", out driveName);
                storage.Add(new StorageReading { Name = Convert.ToString(driveName), TotalGB = ReadNumber(row, "TotalGB"), FreeGB = ReadNumber(row, "FreeGB"), UsedGB = ReadNumber(row, "UsedGB") });
            }
        }
        object captured, caveat; data.TryGetValue("Captured", out captured); data.TryGetValue("Caveat", out caveat);
        double seconds = ReadNumber(data, "SampleSeconds") ?? 0;
        return new CaptureSnapshot { Name = name, StartedUtc = Convert.ToString(captured), FinishedUtc = Convert.ToString(captured), DurationSeconds = seconds, SampleCount = (int)seconds, WorkloadNote = "Live desktop observation; uncontrolled workload", Caveat = Convert.ToString(caveat), Average = average, Storage = storage };
    }
    private static double? ReadNumber(Dictionary<string, object> data, string key)
    {
        object value;
        if (!data.TryGetValue(key, out value) || value == null) return null;
        return Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }
}

public sealed class AverageMetrics
{
    public double? CpuPercent, MemoryUsedGB, DiskReadMBps, DiskWriteMBps, DiskActivePercent, NetworkReceiveMBps, NetworkSendMBps, GpuUtilizationPercent, GpuMemoryUsedMB, GpuTemperatureC, GpuPowerWatts;
    public int CpuSamples, MemorySamples, DiskSamples, NetworkSamples, GpuFreshSamples;
    public double?[] Values() { return new double?[] { CpuPercent, MemoryUsedGB, DiskReadMBps, DiskWriteMBps, DiskActivePercent, NetworkReceiveMBps, NetworkSendMBps, GpuUtilizationPercent, GpuMemoryUsedMB, GpuTemperatureC, GpuPowerWatts }; }
    internal static AverageMetrics FromSamples(List<TelemetrySample> samples)
    {
        AverageMetrics average = new AverageMetrics();
        average.CpuPercent = Mean(samples, delegate(TelemetrySample value) { return value.CpuPercent; });
        average.MemoryUsedGB = Mean(samples, delegate(TelemetrySample value) { return value.MemoryUsedGB; });
        average.DiskReadMBps = Mean(samples, delegate(TelemetrySample value) { return value.DiskReadMBps; });
        average.DiskWriteMBps = Mean(samples, delegate(TelemetrySample value) { return value.DiskWriteMBps; });
        average.DiskActivePercent = Mean(samples, delegate(TelemetrySample value) { return value.DiskActivePercent; });
        average.NetworkReceiveMBps = Mean(samples, delegate(TelemetrySample value) { return value.NetworkReceiveMBps; });
        average.NetworkSendMBps = Mean(samples, delegate(TelemetrySample value) { return value.NetworkSendMBps; });
        average.GpuUtilizationPercent = MeanGpu(samples, delegate(GpuReading value) { return value.UtilizationPercent; });
        average.GpuMemoryUsedMB = MeanGpu(samples, delegate(GpuReading value) { return value.MemoryUsedMB; });
        average.GpuTemperatureC = MeanGpu(samples, delegate(GpuReading value) { return value.TemperatureC; });
        average.GpuPowerWatts = MeanGpu(samples, delegate(GpuReading value) { return value.PowerWatts; });
        foreach (TelemetrySample sample in samples)
        {
            if (sample.CpuPercent.HasValue) average.CpuSamples++;
            if (sample.MemoryUsedGB.HasValue) average.MemorySamples++;
            if (sample.DiskReadMBps.HasValue || sample.DiskWriteMBps.HasValue) average.DiskSamples++;
            if (sample.NetworkReceiveMBps.HasValue || sample.NetworkSendMBps.HasValue) average.NetworkSamples++;
        }
        HashSet<string> gpuTimes = new HashSet<string>();
        foreach (TelemetrySample sample in samples) if (sample.Gpu != null && !sample.Gpu.IsStale && !String.IsNullOrWhiteSpace(sample.Gpu.Timestamp) && gpuTimes.Add(sample.Gpu.Timestamp)) average.GpuFreshSamples++;
        return average;
    }
    private static double? Mean(List<TelemetrySample> samples, Func<TelemetrySample, double?> selector)
    {
        double sum = 0; int count = 0;
        foreach (TelemetrySample sample in samples) { double? value = selector(sample); if (value.HasValue && !Double.IsNaN(value.Value) && !Double.IsInfinity(value.Value)) { sum += value.Value; count++; } }
        return count == 0 ? null : (double?)(sum / count);
    }
    private static double? MeanGpu(List<TelemetrySample> samples, Func<GpuReading, double?> selector)
    {
        // GPU refreshes every five seconds; average unique fresh samples rather than repeated cached copies.
        HashSet<string> times = new HashSet<string>(); double sum = 0; int count = 0;
        foreach (TelemetrySample sample in samples)
        {
            if (sample.Gpu == null || sample.Gpu.IsStale || String.IsNullOrWhiteSpace(sample.Gpu.Timestamp) || !times.Add(sample.Gpu.Timestamp)) continue;
            double? value = selector(sample.Gpu);
            if (value.HasValue && !Double.IsNaN(value.Value) && !Double.IsInfinity(value.Value)) { sum += value.Value; count++; }
        }
        return count == 0 ? null : (double?)(sum / count);
    }
}
