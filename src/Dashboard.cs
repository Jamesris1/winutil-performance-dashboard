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
        ClientSize = new Size(1180, 740);
        MinimumSize = new Size(1050, 680);
        StartPosition = FormStartPosition.CenterScreen;

        TableLayoutPanel frame = Table(2, 1);
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 204));
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(frame);
        Panel side = new Panel { Dock = DockStyle.Fill, BackColor = Sidebar, Padding = new Padding(20, 26, 18, 18) };
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

        Panel main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 10) };
        frame.Controls.Add(main, 1, 0);
        TableLayoutPanel mainLayout = Table(1, 3);
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
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
        mainLayout.Controls.Add(pageHost, 0, 1);
        status.Text = "Ready  ·  Metrics update every second; GPU and drive capacity every five seconds.";
        status.ForeColor = Muted;
        status.Font = new Font("Segoe UI", 8.5F);
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft;
        mainLayout.Controls.Add(status, 0, 2);

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
        timer.Tick += delegate { RequestSample(); };
        if (!previewMode)
        {
            Shown += delegate { RequestSample(); timer.Start(); };
        }
        FormClosed += delegate { timer.Stop(); timer.Dispose(); telemetry.Dispose(); };
    }

    private Panel BuildOverview()
    {
        Panel page = new Panel();
        TableLayoutPanel layout = Table(1, 3);
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
        TableLayoutPanel layout = Table(1, 8);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        layout.Controls.Add(LabelText("Run a chosen profile", 15, Foreground, FontStyle.Bold), 0, 0);
        layout.Controls.Add(LabelText("Optional automation applies your selections immediately after you click the button below.", 9.5F, Muted), 0, 1);
        layout.Controls.Add(LabelText("VENDOR PRESET", 8.5F, Muted, FontStyle.Bold), 0, 2);
        preset.DropDownStyle = ComboBoxStyle.DropDownList;
        preset.Items.AddRange(new object[] { "None — use full WinUtil to choose", "Standard", "Minimal", "Advanced" });
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
        layout.Controls.Add(LabelText("CONFIG FILE OR URL  ·  OPTIONAL", 8.5F, Muted, FontStyle.Bold), 0, 4);
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
        layout.Controls.Add(configRow, 0, 5);
        offline.Text = "Offline flag   ·   not a network sandbox";
        offline.ForeColor = Muted;
        offline.Font = new Font("Segoe UI", 9);
        offline.Dock = DockStyle.Top;
        offline.AutoSize = false;
        offline.Height = 28;
        offline.Margin = new Padding(0);
        layout.Controls.Add(offline, 0, 6);
        applyProfile.Text = "Apply chosen profile";
        applyProfile.Dock = DockStyle.Fill;
        applyProfile.BackColor = Color.FromArgb(38, 72, 86);
        applyProfile.ForeColor = Cyan;
        applyProfile.Margin = new Padding(0, 3, 0, 0);
        applyProfile.Click += delegate { LaunchChosenProfile(); };
        layout.Controls.Add(applyProfile, 0, 7);
        card.Controls.Add(layout);
        new ToolTip().SetToolTip(config, "Optional local JSON configuration path or http(s) URL. Config and Preset combine using WinUtil's original behavior.");
        return card;
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
        Panel page = new Panel();
        TableLayoutPanel layout = Table(1, 3);
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
        Panel page = new Panel();
        TableLayoutPanel layout = Table(1, 5);
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
                if (failure != null) { status.Text = "Some metrics are unavailable: " + failure.Message; return; }
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
        status.Text = "Live  ·  " + DateTime.Now.ToString("HH:mm:ss") + "  ·  Read-only metrics. Activity is not a benchmark score.";
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
        List<string> arguments = new List<string>();
        if (!String.IsNullOrWhiteSpace(config.Text)) { arguments.Add("-Config"); arguments.Add(config.Text.Trim()); }
        if (preset.SelectedIndex != 0) { arguments.Add("-Preset"); arguments.Add(preset.SelectedItem.ToString()); }
        if (offline.Checked) arguments.Add("-Offline");
        try { OpenVendor(Launcher.ParseOptions(arguments.ToArray())); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Profile could not start", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void OpenVendor(Launcher.LaunchOptions options)
    {
        if (previewMode) return;
        fullWinUtil.Enabled = applyProfile.Enabled = false;
        status.Text = "Requesting UAC to start the original WinUtil. Monitoring continues.";
        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                using (Process process = Launcher.StartVendor(options))
                {
                    Post(delegate { fullWinUtil.Enabled = applyProfile.Enabled = true; status.Text = "WinUtil started. Monitoring continues in this dashboard."; });
                    process.WaitForExit();
                    int code = process.ExitCode;
                    Post(delegate {
                        status.Text = code == 0 ? "WinUtil finished. Capture After with the same workload when ready." : "WinUtil returned exit code " + code + ".";
                        if (code != 0) MessageBox.Show(this, "WinUtil's elevated PowerShell process returned exit code " + code + ". Detailed errors are in WinUtil's own logs.", "WinUtil did not complete", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    });
                }
            }
            catch (Exception error)
            {
                Post(delegate {
                    fullWinUtil.Enabled = applyProfile.Enabled = true;
                    Win32Exception windowsError = error as Win32Exception;
                    if (windowsError != null && windowsError.NativeErrorCode == 1223) { status.Text = "UAC canceled. No WinUtil action was started."; return; }
                    status.Text = "WinUtil could not start.";
                    MessageBox.Show(this, error.Message, "WinUtil could not start", MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            }
        });
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
                status.Text = "Exported comparison to " + dialog.FileName;
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
        status.Text = "PREVIEW  ·  Initial state; readings begin when you open the dashboard. No WinUtil action executed.";
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
        if (disposing) { if (timer != null) timer.Dispose(); if (telemetry != null) telemetry.Dispose(); }
        base.Dispose(disposing);
    }

    private static TableLayoutPanel Table(int columns, int rows)
    {
        TableLayoutPanel layout = new TableLayoutPanel { ColumnCount = columns, RowCount = rows, Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0), BackColor = Color.Transparent };
        if (columns == 1) layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return layout;
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
    public int Value { get { return value; } set { this.value = Math.Min(Maximum, Math.Max(Minimum, value)); Invalidate(); } }
    internal ThinProgress() { DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.FromArgb(28, 45, 64));
        if (value > Minimum)
            using (Brush accent = new SolidBrush(Dashboard.Cyan)) e.Graphics.FillRectangle(accent, 0, 0, Width * (value - Minimum) / Math.Max(1, Maximum - Minimum), Height);
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
