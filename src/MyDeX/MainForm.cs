using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MyDeX;

/// <summary>
/// Main window: layout, settings, device list and the DeX session. Apps, Wi-Fi, files, battery,
/// hotkey, updates and the tray menu live in MainForm.Features.cs.
/// </summary>
public sealed partial class MainForm : Form
{
    const string ScrcpyDownloadUrl = "https://github.com/Genymobile/scrcpy/releases/latest";
    static readonly Color Accent = Color.FromArgb(22, 110, 230);
    static readonly Color CardBack = Color.FromArgb(240, 245, 252);

    readonly AppSettings settings = AppSettings.Load();
    readonly bool startInTray;

    AdbClient? adb;
    string? scrcpyExe;
    string? scrcpyFolder;
    DexSession? session;
    PhoneDevice? sessionDevice;
    DateTime sessionStartedAt;
    bool starting, refreshing, exiting, trayTipShown;
    string lastDeviceKey = "";
    HashSet<string> readySerials = [];
    List<PhoneDevice> lastDevices = [];

    // Auto-reconnect: the phone DeX was running on when the connection dropped.
    const int MaxReconnectAttempts = 5;
    bool userStopping;
    string? reconnectSerial;
    int reconnectAttempts;
    bool lastStartWasReconnect;
    DateTime reconnectSeenSince = DateTime.MaxValue;
    DateTime lastWifiRetry;

    readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    readonly TabPage tabHome = new("Home"), tabApps = new("Apps"), tabWireless = new("Wireless"), tabSettings = new("Settings");

    // Home
    readonly ComboBox cmbDevices = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
    readonly Button btnRefresh = new() { Text = "Refresh", AutoSize = true };
    readonly StatusDot statusDot = new();
    readonly Label lblHomeTitle = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 14f) };
    readonly Label lblHomeDetail = new() { AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = SystemColors.GrayText };
    readonly Button btnToggle = new() { Dock = DockStyle.Fill, Height = 60, Font = new Font("Segoe UI Semibold", 14f), FlatStyle = FlatStyle.Flat };
    readonly FlowLayoutPanel flowModes = new() { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 6) };
    readonly FlowLayoutPanel flowTiles = new() { AutoSize = true, WrapContents = true, MaximumSize = new Size(600, 0) };
    readonly Button btnPhoneScreen = new() { Text = "📱  Show my phone screen too", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
    readonly Label lblHomeTip = new() { AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = SystemColors.GrayText };

    // Apps
    readonly TextBox txtAppSearch = new() { Width = 280, PlaceholderText = "🔍  Search your apps..." };
    readonly Button btnLoadApps = new() { Text = "Reload list", AutoSize = true };
    readonly ListView lvApps = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
        HeaderStyle = ColumnHeaderStyle.None, HideSelection = false,
    };
    readonly Button btnOpenApp = new() { Text = "▶  Open in its own window", AutoSize = true, Font = new Font("Segoe UI Semibold", 9.5f) };
    readonly Button btnPinApp = new() { Text = "★  Add to Home", AutoSize = true };
    readonly ComboBox cmbAppShape = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    readonly CheckBox chkMouseCapture = new() { Text = "Lock mouse in the window (shooters – Left Alt frees it)", AutoSize = true };
    readonly ListBox lstOpenWindows = new() { Height = 64, Width = 330, IntegralHeight = false };
    readonly Button btnCloseWindow = new() { Text = "Close", AutoSize = true };
    readonly Button btnCloseApps = new() { Text = "Close all", AutoSize = true };
    List<PhoneApp> phoneApps = [];
    string? appsSerial;
    readonly List<AppWindow> appWindows = [];

    // Settings: picture (the active profile)...
    readonly ComboBox cmbProfile = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    readonly Button btnSaveProfileAs = new() { Text = "Save as new...", AutoSize = true };
    readonly Button btnDeleteProfile = new() { Text = "Delete", AutoSize = true };
    DisplayProfile? shownProfile;
    bool switchingProfile;
    readonly ComboBox cmbResolution = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 160 };
    readonly Button btnFitScreen = new() { Text = "Fit my screen", AutoSize = true };
    readonly NumericUpDown numDpi = new() { Minimum = 80, Maximum = 640, Increment = 10, Width = 90 };
    readonly NumericUpDown numFps = new() { Minimum = 15, Maximum = 144, Width = 90 };
    readonly NumericUpDown numBitrate = new() { Minimum = 2, Maximum = 100, Width = 90 };
    readonly ComboBox cmbCodec = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    readonly CheckBox chkAudio = new() { Text = "Play the phone's sound on the PC", AutoSize = true };
    readonly CheckBox chkFullscreen = new() { Text = "Open DeX full screen (Alt+F toggles)", AutoSize = true };
    readonly CheckBox chkOnTop = new() { Text = "Keep MyDeX windows on top", AutoSize = true };
    // ...and settings shared by all profiles
    readonly ComboBox cmbController = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    readonly CheckBox chkRecord = new() { Text = "Record DeX and app windows to video (MP4)", AutoSize = true };
    readonly TextBox txtRecordFolder = new() { Width = 250 };
    readonly Button btnRecordBrowse = new() { Text = "Browse...", AutoSize = true };
    readonly Button btnRecordOpen = new() { Text = "Open folder", AutoSize = true };
    readonly CheckBox chkKeepApps = new() { Text = "When DeX stops, move its open apps to the phone instead of closing them", AutoSize = true };
    readonly CheckBox chkAutoStart = new() { Text = "Start DeX by itself when I plug in my phone", AutoSize = true };
    readonly CheckBox chkAutoReconnect = new() { Text = "Bring DeX back by itself if the cable or Wi-Fi drops", AutoSize = true };
    readonly CheckBox chkHotkey = new() { Text = $"{GlobalHotkey.Description} starts/stops DeX from anywhere in Windows", AutoSize = true };
    readonly CheckBox chkStartWithWindows = new() { Text = "Start MyDeX with Windows (in the tray)", AutoSize = true };
    readonly CheckBox chkMinimizeToTray = new() { Text = "Closing this window keeps MyDeX running in the tray", AutoSize = true };
    readonly CheckBox chkStayAwake = new() { Text = "Keep the phone awake while plugged in", AutoSize = true };
    readonly CheckBox chkShareClipboard = new() { Text = "Share copy && paste between the PC and the phone", AutoSize = true };
    readonly CheckBox chkWifiOffOnExit = new() { Text = "Turn off Wi-Fi mode on the phone when MyDeX closes (safer)", AutoSize = true };
    readonly NumericUpDown numHeat = new() { Minimum = 35, Maximum = 60, Width = 70 };
    readonly ComboBox cmbMethod = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
    readonly TextBox txtScrcpyFolder = new() { Width = 300, PlaceholderText = "(auto-detect)" };
    readonly Button btnBrowse = new() { Text = "Browse...", AutoSize = true };
    readonly Label lblTools = new() { AutoSize = true, MaximumSize = new Size(520, 0) };
    readonly CheckBox chkUpdates = new() { Text = "Check for updates once a day", AutoSize = true };
    readonly Button btnCheckUpdates = new() { Text = "Check now", AutoSize = true };
    readonly Button btnUpdateScrcpy = new() { Text = "Update scrcpy", AutoSize = true, Visible = false };
    readonly LinkLabel lnkMyDexUpdate = new() { AutoSize = true, Visible = false };
    readonly Label lblUpdates = new() { AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = SystemColors.GrayText };
    readonly Button btnShowLog = new() { Text = "Show log", AutoSize = true };

    // Wireless
    readonly Button btnToWifi = new() { Text = "Switch my phone to Wi-Fi (unencrypted)", AutoSize = true };
    readonly TextBox txtConnect = new() { Width = 220, PlaceholderText = "192.168.1.20:5555" };
    readonly Button btnConnect = new() { Text = "Connect", AutoSize = true };
    readonly TextBox txtPairAddress = new() { Width = 220, PlaceholderText = "192.168.1.20:37123" };
    readonly TextBox txtPairCode = new() { Width = 100, PlaceholderText = "123456" };
    readonly Button btnPair = new() { Text = "Pair", AutoSize = true };
    readonly Button btnDisconnect = new() { Text = "Disconnect Wi-Fi phones", AutoSize = true };

    // Status bar + log
    readonly StatusStrip statusBar = new() { SizingGrip = false };
    readonly ToolStripStatusLabel lblStatus = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripStatusLabel lblBattery = new();
    readonly TextBox txtLog = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 9f), BackColor = SystemColors.Window,
    };
    Form? logWindow;

    readonly NotifyIcon tray = new();
    readonly ToolStripMenuItem miStart = new("Start DeX");
    readonly ToolStripMenuItem miStop = new("Stop DeX");
    readonly ToolStripMenuItem miProfiles = new("Mode");
    readonly ToolStripMenuItem miRecentApps = new("Open app");
    readonly System.Windows.Forms.Timer pollTimer = new() { Interval = 3000 };
    readonly System.Windows.Forms.Timer batteryTimer = new() { Interval = 30_000 };

    static readonly string[] ResolutionPresets =
        ["1280x720", "1600x900", "1920x1080", "2560x1080", "2560x1440", "3440x1440", "3840x2160"];

    public MainForm(bool startInTray)
    {
        this.startInTray = startInTray;
        Text = $"MyDeX {UpdateChecker.CurrentVersion.ToString(3)}";
        Icon = AppIcon.Create();
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(660, 640);
        MinimumSize = new Size(600, 560);

        if (startInTray)
        {
            // Start hidden without a flash; OnShown hides the form and restores these.
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            Opacity = 0;
        }

        BuildUi();
        LoadSettingsIntoUi();
        SetupTray();
        WireEvents();
        EnableFileDrop(this);
        UpdateHome();
    }

    // ───────────────────────── UI construction ─────────────────────────

    void BuildUi()
    {
        BuildHomeTab();
        BuildAppsTab();
        BuildWirelessTab();
        BuildSettingsTab();
        tabs.TabPages.AddRange([tabHome, tabApps, tabWireless, tabSettings]);

        statusBar.Items.Add(lblStatus);
        statusBar.Items.Add(lblBattery);

        var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 10, 10, 4) };
        root.Controls.Add(tabs);
        Controls.Add(root);
        Controls.Add(statusBar);
    }

    void BuildHomeTab()
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = CardBack, Padding = new Padding(14, 12, 14, 12) };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.Controls.Add(statusDot, 0, 0);
        card.SetRowSpan(statusDot, 2);
        card.Controls.Add(lblHomeTitle, 1, 0);
        card.Controls.Add(lblHomeDetail, 1, 1);

        var phoneRow = Flow(new Label { Text = "Phone:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, cmbDevices, btnRefresh);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12), AutoScroll = true };
        layout.Controls.Add(card);
        layout.Controls.Add(Spacer(6));
        layout.Controls.Add(btnToggle);
        layout.Controls.Add(Spacer(10));
        layout.Controls.Add(Heading("Mode"));
        layout.Controls.Add(flowModes);
        layout.Controls.Add(Heading("Your apps"));
        layout.Controls.Add(Hint("Opens in its own window next to DeX. Add more on the Apps tab (★)."));
        layout.Controls.Add(flowTiles);
        layout.Controls.Add(Spacer(6));
        layout.Controls.Add(btnPhoneScreen);
        layout.Controls.Add(Spacer(10));
        layout.Controls.Add(phoneRow);
        layout.Controls.Add(lblHomeTip);
        card.Dock = DockStyle.Fill;
        tabHome.Controls.Add(layout);

        btnToggle.FlatAppearance.BorderSize = 0;
    }

    void BuildAppsTab()
    {
        cmbAppShape.Items.AddRange(["Landscape", "Portrait"]);
        lvApps.Columns.Add("App", 400);

        var openWindows = new GroupBox { Text = "Open windows (select one and click Close)", Dock = DockStyle.Fill, Height = 100, Padding = new Padding(8) };
        var windowsRow = Flow(lstOpenWindows, btnCloseWindow, btnCloseApps);
        windowsRow.Dock = DockStyle.Fill;   // keeps the list below the box's title
        openWindows.Controls.Add(windowsRow);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(Hint("Pick an app or game and open it in its own window – you can open several at once, " +
                                 "next to DeX. Double-click to open. ★ puts it on the Home tab."), 0, 0);
        layout.Controls.Add(Flow(txtAppSearch, btnLoadApps), 0, 1);
        layout.Controls.Add(lvApps, 0, 2);
        layout.Controls.Add(Flow(btnOpenApp, btnPinApp,
            new Label { Text = "Shape:", AutoSize = true, Margin = new Padding(12, 7, 3, 3) }, cmbAppShape), 0, 3);
        layout.Controls.Add(chkMouseCapture, 0, 4);
        layout.Controls.Add(openWindows, 0, 5);
        tabApps.Controls.Add(layout);
    }

    void BuildWirelessTab()
    {
        var grid = FormGrid();
        AddFull(grid, Hint("The PC and the phone must be on the same Wi-Fi. Use this only on a network you trust " +
                           "(your home) – never on public, hotel or café Wi-Fi."));

        AddFull(grid, Heading("Recommended: encrypted wireless debugging (Android 11+)"));
        AddFull(grid, Hint("1. On the phone: Developer options › Wireless debugging › on › Pair device with pairing code.\n" +
                           "2. Type the address and code it shows, then click Pair.\n" +
                           "3. Type the IP address & port shown on the Wireless debugging screen below, then click Connect."));
        AddRow(grid, "Pairing address:", txtPairAddress);
        AddRow(grid, "Pairing code:", Flow(txtPairCode, btnPair));
        AddRow(grid, "Phone address:", Flow(txtConnect, btnConnect));

        AddFull(grid, Heading("Quick switch (unencrypted)"));
        AddFull(grid, WarningHint("⚠  Faster to set up, but the connection is not encrypted: someone on the same network " +
                                  "could see the screen and what you type. The phone keeps listening until it restarts, " +
                                  "you click Disconnect, or MyDeX closes (see Settings › Privacy & safety)."));
        AddFull(grid, Hint("Plug in your phone, click the button, then unplug the cable."));
        AddFull(grid, btnToWifi);

        AddFull(grid, Spacer(8));
        AddFull(grid, btnDisconnect);
        AddFull(grid, Hint("Disconnects every Wi-Fi phone and switches it back to USB-only."));
        tabWireless.AutoScroll = true;
        tabWireless.Controls.Add(grid);
    }

    void BuildSettingsTab()
    {
        cmbResolution.Items.AddRange(ResolutionPresets);
        var screen = Screen.PrimaryScreen?.Bounds.Size;
        if (screen is { } s && !ResolutionPresets.Contains($"{s.Width}x{s.Height}"))
            cmbResolution.Items.Add($"{s.Width}x{s.Height}");
        cmbCodec.Items.AddRange(["h264", "h265", "av1"]);
        cmbController.Items.AddRange(["Off", "The DeX window", "App windows"]);
        cmbMethod.Items.AddRange(["Invisible virtual screen (recommended)", "Simulated display (shows a small copy on the phone)"]);

        var grid = FormGrid();
        AddFull(grid, Heading("Picture"));
        AddRow(grid, "Mode:", Flow(cmbProfile, btnSaveProfileAs, btnDeleteProfile));
        AddRow(grid, "Resolution:", Flow(cmbResolution, btnFitScreen));
        AddRow(grid, "Text size (DPI):", Flow(numDpi, new Label { Text = "lower = more fits on screen", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 7, 3, 3) }));
        AddRow(grid, "Smoothness (max FPS):", numFps);
        AddRow(grid, "Quality (Mbps):", numBitrate);
        AddRow(grid, "Video codec:", cmbCodec);
        AddFull(grid, chkFullscreen);
        AddFull(grid, chkOnTop);
        AddFull(grid, Hint("Picture settings are saved in the selected mode. Changes apply the next time you start DeX."));

        AddFull(grid, Heading("Sound & game controller"));
        AddFull(grid, chkAudio);
        AddRow(grid, "Controller goes to:", cmbController);
        AddFull(grid, Hint("Plug an Xbox/PlayStation controller into the PC and the phone sees a real gamepad. " +
                           "Android plays the phone's sound through one window only – usually the DeX window."));

        AddFull(grid, Heading("Recording"));
        AddFull(grid, chkRecord);
        AddRow(grid, "Save in:", Flow(txtRecordFolder, btnRecordBrowse, btnRecordOpen));

        AddFull(grid, Heading("Starting & stopping"));
        AddFull(grid, chkAutoStart);
        AddFull(grid, chkAutoReconnect);
        AddFull(grid, chkKeepApps);
        AddFull(grid, chkHotkey);
        AddFull(grid, chkStartWithWindows);
        AddFull(grid, chkMinimizeToTray);
        AddFull(grid, chkStayAwake);
        AddRow(grid, "Heat warning at:", Flow(numHeat, new Label { Text = "°C (phone temperature)", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(6, 7, 3, 3) }));

        AddFull(grid, Heading("Privacy & safety"));
        AddFull(grid, chkShareClipboard);
        AddFull(grid, chkWifiOffOnExit);
        AddFull(grid, Hint("When on, anything you copy on the PC – passwords too – is also copied to the phone, and back. " +
                           "MyDeX sends nothing to the internet except the optional update check on github.com. " +
                           "USB debugging lets a connected PC control the phone: only tap Allow on PCs you trust, " +
                           "and turn it off in Developer options when you don't need it."));

        AddFull(grid, Heading("Advanced"));
        AddRow(grid, "DeX screen type:", cmbMethod);
        AddRow(grid, "scrcpy folder:", Flow(txtScrcpyFolder, btnBrowse));
        AddFull(grid, lblTools);
        var link = new LinkLabel { Text = "Download scrcpy (includes adb)", AutoSize = true };
        link.LinkClicked += (_, _) => OpenUrl(ScrcpyDownloadUrl);
        AddFull(grid, link);
        AddFull(grid, Flow(chkUpdates, btnCheckUpdates, btnUpdateScrcpy));
        AddFull(grid, lblUpdates);
        AddFull(grid, lnkMyDexUpdate);
        AddFull(grid, btnShowLog);

        tabSettings.AutoScroll = true;
        tabSettings.Controls.Add(grid);
    }

    static TableLayoutPanel FormGrid()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(10) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }

    static void AddRow(TableLayoutPanel grid, string label, Control control)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label { Text = label, AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 12, 3) }, 0, row);
        control.Anchor = AnchorStyles.Left;
        grid.Controls.Add(control, 1, row);
    }

    static void AddFull(TableLayoutPanel grid, Control control)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(control, 0, row);
        grid.SetColumnSpan(control, 2);
    }

    static Label Heading(string text) =>
        new() { Text = text, AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI Semibold", 11f), ForeColor = Accent, Margin = new Padding(3, 12, 3, 4) };

    static Label Hint(string text) =>
        new() { Text = text, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(580, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 2, 3, 6) };

    static Label WarningHint(string text)
    {
        var label = Hint(text);
        label.ForeColor = Color.FromArgb(160, 90, 0);
        return label;
    }

    static Control Spacer(int height = 8) => new Panel { Height = height, Width = 1, Margin = Padding.Empty };

    static FlowLayoutPanel Flow(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        flow.Controls.AddRange(controls);
        return flow;
    }

    void WireEvents()
    {
        pollTimer.Tick += async (_, _) => await RefreshDevicesAsync();
        batteryTimer.Tick += async (_, _) => await UpdateBatteryAsync();
        btnRefresh.Click += async (_, _) => { lastDeviceKey = ""; await RefreshDevicesAsync(); };
        cmbDevices.SelectedIndexChanged += async (_, _) => { UpdateButtons(); await UpdateBatteryAsync(); };
        btnToggle.Click += async (_, _) => await ToggleDexAsync();
        btnPhoneScreen.Click += async (_, _) => await OpenAppAsync(AppWindow.PhoneScreen);
        tabs.SelectedIndexChanged += async (_, _) =>
        {
            if (tabs.SelectedTab == tabApps && SelectedReadyDevice() is { } phone && (phoneApps.Count == 0 || appsSerial != phone.Serial))
                await LoadAppsAsync();
        };

        cmbProfile.SelectedIndexChanged += (_, _) =>
        {
            if (!switchingProfile && cmbProfile.SelectedItem is string name && name != shownProfile?.Name)
                SwitchProfile(name);
        };
        btnSaveProfileAs.Click += (_, _) => SaveProfileAs();
        btnDeleteProfile.Click += (_, _) => DeleteProfile();
        btnFitScreen.Click += (_, _) => FitScreen();
        btnRecordBrowse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Where should recordings be saved?", SelectedPath = txtRecordFolder.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                txtRecordFolder.Text = dialog.SelectedPath;
                SaveUiToSettings();
            }
        };
        btnRecordOpen.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(txtRecordFolder.Text);
                OpenUrl(txtRecordFolder.Text);
            }
            catch (Exception ex) { Log("Could not open the recordings folder: " + ex.Message); }
        };

        btnLoadApps.Click += async (_, _) => await LoadAppsAsync();
        txtAppSearch.TextChanged += (_, _) => FilterApps();
        txtAppSearch.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && lvApps.Items.Count > 0)
            {
                e.SuppressKeyPress = true;
                lvApps.Items[0].Selected = true;
                await OpenSelectedAppAsync();
            }
        };
        lvApps.DoubleClick += async (_, _) => await OpenSelectedAppAsync();
        lvApps.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) await OpenSelectedAppAsync(); };
        lvApps.SelectedIndexChanged += (_, _) => UpdatePinButton();
        lvApps.Resize += (_, _) => lvApps.Columns[0].Width = Math.Max(100, lvApps.ClientSize.Width - 4);
        btnOpenApp.Click += async (_, _) => await OpenSelectedAppAsync();
        btnPinApp.Click += (_, _) => TogglePinSelected();
        btnCloseWindow.Click += async (_, _) =>
        {
            if (lstOpenWindows.SelectedItem is AppWindowItem item)
                await item.Window.StopAsync();
        };
        btnCloseApps.Click += async (_, _) => await CloseAllAppWindowsAsync();

        btnToWifi.Click += async (_, _) => await SwitchToWifiAsync();
        btnConnect.Click += async (_, _) => await ConnectWifiAsync(txtConnect.Text);
        btnPair.Click += async (_, _) => await PairAsync();
        btnDisconnect.Click += async (_, _) => await DisconnectWifiAsync();

        chkStartWithWindows.CheckedChanged += (_, _) =>
        {
            try { StartupRegistration.Set(chkStartWithWindows.Checked); }
            catch (Exception ex) { Log("Could not change the Windows startup entry: " + ex.Message); }
            SaveUiToSettings();
        };
        chkHotkey.CheckedChanged += (_, _) =>
        {
            SaveUiToSettings();
            ApplyHotkey();
        };
        foreach (var box in new[] { chkAutoStart, chkAutoReconnect, chkMinimizeToTray, chkUpdates, chkRecord, chkMouseCapture, chkKeepApps, chkStayAwake, chkShareClipboard, chkWifiOffOnExit })
            box.CheckedChanged += (_, _) => SaveUiToSettings();
        foreach (var combo in new[] { cmbController, cmbMethod, cmbAppShape })
            combo.SelectedIndexChanged += (_, _) => SaveUiToSettings();
        numHeat.ValueChanged += (_, _) => SaveUiToSettings();

        btnBrowse.Click += async (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Pick the folder that contains scrcpy.exe" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            txtScrcpyFolder.Text = dialog.SelectedPath;
            SaveUiToSettings();
            LocateTools();
            lastDeviceKey = "";
            await RefreshDevicesAsync();
        };
        btnCheckUpdates.Click += async (_, _) => await CheckForUpdatesAsync(quiet: false);
        btnUpdateScrcpy.Click += async (_, _) => await UpdateScrcpyAsync();
        lnkMyDexUpdate.LinkClicked += (_, _) =>
        {
            if (lnkMyDexUpdate.Tag is string url && UpdateChecker.IsTrustedGitHubUrl(url, UpdateChecker.MyDexRepo))
                OpenUrl(url);
        };
        btnShowLog.Click += (_, _) => ShowLog();
    }

    // ───────────────────────── Home ─────────────────────────

    /// <summary>Brings the Home tab (status card, big button, modes, app tiles) up to date.</summary>
    void UpdateHome()
    {
        bool running = session?.IsRunning == true;
        var device = SelectedReadyDevice();
        string batteryText = battery == null ? "" : $" · battery {battery.Percent}%";

        if (starting)
            SetHome(StatusDot.Busy, "Starting DeX...", "This takes a few seconds. If your phone asks to start DeX, tap Start.");
        else if (running && sessionDevice != null)
            SetHome(StatusDot.Good, "DeX is running", $"On {sessionDevice.Model} ({(sessionDevice.IsWifi ? "Wi-Fi" : "USB")}){batteryText}. " +
                                                      "Your phone stays free to use at the same time.");
        else if (reconnectSerial != null)
            SetHome(StatusDot.Busy, "Waiting for your phone...", "The connection dropped. DeX comes back by itself when the phone is connected again.");
        else if (scrcpyExe == null)
            SetHome(StatusDot.Bad, "scrcpy is missing", "MyDeX needs scrcpy. Settings › Advanced shows where to get it.");
        else if (device != null)
            SetHome(StatusDot.Ready, "Ready", $"{device.Model} is connected ({(device.IsWifi ? "Wi-Fi" : "USB")}){batteryText}.");
        else if (lastDevices.Any(d => d.State == "unauthorized"))
            SetHome(StatusDot.Busy, "Allow access on your phone", "Unlock your phone and tap Allow on \"Allow USB debugging?\".");
        else
            SetHome(StatusDot.Bad, "Connect your phone",
                "1. On the phone, turn on Developer options › USB debugging.\n2. Plug it into this PC with a USB cable.\n3. Tap Allow on the phone.");

        btnToggle.Text = starting ? "✕  Cancel" : running || reconnectSerial != null ? "■  Stop DeX" : "▶  Start DeX";
        btnToggle.BackColor = starting || running || reconnectSerial != null ? Color.FromArgb(200, 60, 60) : Accent;
        btnToggle.ForeColor = Color.White;
        btnToggle.Enabled = starting || running || reconnectSerial != null || (device != null && scrcpyExe != null);
        btnPhoneScreen.Enabled = device != null && scrcpyExe != null;

        lblHomeTip.Text = "Tip: drop files anywhere on this window to send them to your phone (Download › MyDeX). " +
                          $"{GlobalHotkey.Description} starts and stops DeX from anywhere.";
    }

    readonly ToolTip tileTips = new();

    /// <summary>Removed controls keep their window handles until disposed, so rebuilt rows must dispose the old ones.</summary>
    static void ClearAndDispose(Control parent)
    {
        var old = parent.Controls.Cast<Control>().ToList();
        parent.Controls.Clear();
        foreach (var control in old)
            control.Dispose();
    }

    void SetHome(Color dot, string title, string detail)
    {
        statusDot.Color = dot;
        lblHomeTitle.Text = title;
        lblHomeDetail.Text = detail;
    }

    void BuildModeButtons()
    {
        flowModes.SuspendLayout();
        ClearAndDispose(flowModes);
        foreach (var profile in settings.Profiles)
        {
            string name = profile.Name;
            var button = new RadioButton
            {
                Text = name, Appearance = Appearance.Button, AutoSize = false, Size = new Size(120, 36),
                TextAlign = ContentAlignment.MiddleCenter, FlatStyle = FlatStyle.Flat, Checked = name == settings.Profile.Name,
                Margin = new Padding(0, 0, 6, 0),
            };
            button.FlatAppearance.CheckedBackColor = Accent;
            button.ForeColor = button.Checked ? Color.White : SystemColors.ControlText;
            button.CheckedChanged += (_, _) =>
            {
                button.ForeColor = button.Checked ? Color.White : SystemColors.ControlText;
                if (button.Checked && name != settings.Profile.Name)
                    SwitchProfile(name);
            };
            flowModes.Controls.Add(button);
        }
        flowModes.ResumeLayout();
    }

    void BuildAppTiles()
    {
        flowTiles.SuspendLayout();
        tileTips.RemoveAll();
        ClearAndDispose(flowTiles);
        var apps = settings.PinnedApps.Count > 0 ? settings.PinnedApps : settings.RecentApps.Take(4).ToList();
        foreach (var app in apps)
        {
            var tile = new Button { Text = app.Name, Size = new Size(136, 52), Margin = new Padding(0, 0, 6, 6), Tag = app, AutoEllipsis = true };
            tile.Click += async (_, _) => await OpenAppAsync(app);
            tileTips.SetToolTip(tile, $"Open {app.Name} in its own window");
            flowTiles.Controls.Add(tile);
        }
        var add = new Button { Text = "+  Add apps", Size = new Size(136, 52), Margin = new Padding(0, 0, 6, 6), ForeColor = Accent };
        add.Click += (_, _) => tabs.SelectedTab = tabApps;
        flowTiles.Controls.Add(add);
        flowTiles.ResumeLayout();
    }

    // ───────────────────────── Settings <-> UI ─────────────────────────

    void LoadSettingsIntoUi()
    {
        RefreshProfileList();
        ShowProfile(settings.Profile);
        cmbMethod.SelectedIndex = settings.Method == DisplayMethod.ScrcpyVirtualDisplay ? 0 : 1;
        cmbController.SelectedIndex = (int)settings.Controller;
        chkKeepApps.Checked = settings.KeepAppsOnStop;
        chkStayAwake.Checked = settings.StayAwake;
        chkShareClipboard.Checked = settings.ShareClipboard;
        chkWifiOffOnExit.Checked = settings.WifiOffOnExit;
        chkRecord.Checked = settings.RecordSessions;
        txtRecordFolder.Text = settings.RecordingsFolder;
        cmbAppShape.SelectedIndex = (int)settings.AppShape;
        chkMouseCapture.Checked = settings.AppMouseCapture;
        chkStartWithWindows.Checked = settings.StartWithWindows;
        chkAutoStart.Checked = settings.AutoStartDex;
        chkAutoReconnect.Checked = settings.AutoReconnect;
        chkHotkey.Checked = settings.HotkeyEnabled;
        chkMinimizeToTray.Checked = settings.MinimizeToTray;
        numHeat.Value = Math.Clamp(settings.HeatWarningC, (int)numHeat.Minimum, (int)numHeat.Maximum);
        chkUpdates.Checked = settings.CheckForUpdates;
        txtScrcpyFolder.Text = settings.ScrcpyFolder ?? "";
        txtConnect.Text = settings.LastWifiAddress ?? "";
        BuildAppTiles();
        UpdateAppWindowsList();
    }

    void RefreshProfileList()
    {
        switchingProfile = true;
        cmbProfile.Items.Clear();
        foreach (var profile in settings.Profiles)
            cmbProfile.Items.Add(profile.Name);
        cmbProfile.SelectedItem = settings.Profile.Name;
        btnDeleteProfile.Enabled = settings.Profiles.Count > 1;
        switchingProfile = false;
        BuildModeButtons();
    }

    void ShowProfile(DisplayProfile profile)
    {
        shownProfile = profile;
        cmbResolution.Text = $"{profile.Width}x{profile.Height}";
        numDpi.Value = Math.Clamp(profile.Dpi, (int)numDpi.Minimum, (int)numDpi.Maximum);
        numFps.Value = Math.Clamp(profile.MaxFps, (int)numFps.Minimum, (int)numFps.Maximum);
        numBitrate.Value = Math.Clamp(profile.BitrateMbps, (int)numBitrate.Minimum, (int)numBitrate.Maximum);
        cmbCodec.SelectedItem = cmbCodec.Items.Contains(profile.VideoCodec) ? profile.VideoCodec : "h264";
        chkAudio.Checked = profile.Audio;
        chkFullscreen.Checked = profile.Fullscreen;
        chkOnTop.Checked = profile.AlwaysOnTop;
    }

    void SaveProfileFields(DisplayProfile profile)
    {
        var match = ResolutionPattern().Match(cmbResolution.Text);
        if (match.Success)
        {
            profile.Width = int.Parse(match.Groups[1].Value);
            profile.Height = int.Parse(match.Groups[2].Value);
        }
        else
        {
            cmbResolution.Text = $"{profile.Width}x{profile.Height}";
        }
        profile.Dpi = (int)numDpi.Value;
        profile.MaxFps = (int)numFps.Value;
        profile.BitrateMbps = (int)numBitrate.Value;
        profile.VideoCodec = cmbCodec.SelectedItem as string ?? "h264";
        profile.Audio = chkAudio.Checked;
        profile.Fullscreen = chkFullscreen.Checked;
        profile.AlwaysOnTop = chkOnTop.Checked;
    }

    void SwitchProfile(string name)
    {
        if (shownProfile != null)
            SaveProfileFields(shownProfile);
        settings.ActiveProfile = name;
        ShowProfile(settings.Profile);
        switchingProfile = true;
        cmbProfile.SelectedItem = name;
        switchingProfile = false;
        foreach (RadioButton b in flowModes.Controls)
            if (b.Text == name && !b.Checked) b.Checked = true;
        settings.Save();
        string note = session?.IsRunning == true ? " – applies the next time you start DeX" : "";
        SetStatus($"Mode: {name}{note}.");
        Log($"Mode: {name}{note}");
    }

    void SaveProfileAs()
    {
        string? name = Prompt.Ask(this, "Save mode", "Name for the new mode:", $"{settings.Profile.Name} copy");
        if (name == null) return;
        if (settings.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, $"A mode called \"{name}\" already exists.", "MyDeX");
            return;
        }
        var profile = new DisplayProfile();
        SaveProfileFields(profile);
        profile.Name = name;
        settings.Profiles.Add(profile);
        settings.ActiveProfile = name;
        ShowProfile(profile);
        RefreshProfileList();
        settings.Save();
    }

    void DeleteProfile()
    {
        if (settings.Profiles.Count <= 1) return;
        var profile = settings.Profile;
        if (MessageBox.Show(this, $"Delete the \"{profile.Name}\" mode?", "MyDeX", MessageBoxButtons.YesNo) != DialogResult.Yes)
            return;
        settings.Profiles.Remove(profile);
        settings.ActiveProfile = settings.Profiles[0].Name;
        ShowProfile(settings.Profile);
        RefreshProfileList();
        settings.Save();
    }

    /// <summary>Sets resolution and DPI to match the monitor MyDeX is on, including Windows' scaling.</summary>
    void FitScreen()
    {
        var bounds = Screen.FromControl(this).Bounds;   // physical pixels (the app is per-monitor DPI aware)
        double scale = DeviceDpi / 96.0;
        cmbResolution.Text = $"{bounds.Width}x{bounds.Height}";
        numDpi.Value = DisplayProfile.DpiForWindowsScale(scale);
        SaveUiToSettings();
        SetStatus($"Fitted to this screen: {bounds.Width}x{bounds.Height} at {scale:P0} scaling.");
    }

    void SaveUiToSettings()
    {
        if (shownProfile != null)
            SaveProfileFields(shownProfile);
        settings.Method = cmbMethod.SelectedIndex == 1 ? DisplayMethod.SimulatedDisplay : DisplayMethod.ScrcpyVirtualDisplay;
        settings.Controller = (ControllerTarget)Math.Max(0, cmbController.SelectedIndex);
        settings.KeepAppsOnStop = chkKeepApps.Checked;
        settings.StayAwake = chkStayAwake.Checked;
        settings.ShareClipboard = chkShareClipboard.Checked;
        settings.WifiOffOnExit = chkWifiOffOnExit.Checked;
        settings.RecordSessions = chkRecord.Checked;
        if (!string.IsNullOrWhiteSpace(txtRecordFolder.Text))
            settings.RecordingsFolder = txtRecordFolder.Text.Trim();
        settings.AppShape = cmbAppShape.SelectedIndex == 1 ? AppWindowShape.Portrait : AppWindowShape.Landscape;
        settings.AppMouseCapture = chkMouseCapture.Checked;
        settings.StartWithWindows = chkStartWithWindows.Checked;
        settings.AutoStartDex = chkAutoStart.Checked;
        settings.AutoReconnect = chkAutoReconnect.Checked;
        settings.HotkeyEnabled = chkHotkey.Checked;
        settings.MinimizeToTray = chkMinimizeToTray.Checked;
        settings.HeatWarningC = (int)numHeat.Value;
        settings.CheckForUpdates = chkUpdates.Checked;
        settings.ScrcpyFolder = string.IsNullOrWhiteSpace(txtScrcpyFolder.Text) ? null : txtScrcpyFolder.Text.Trim();
        settings.Save();
    }

    [GeneratedRegex(@"^\s*(\d{3,5})\s*[xX×]\s*(\d{3,5})")]
    private static partial Regex ResolutionPattern();

    // ───────────────────────── Lifecycle ─────────────────────────

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (startInTray)
        {
            Hide();
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            Opacity = 1;
        }

        // Keep the Run entry pointing at the current exe in case the app folder moved.
        if (settings.StartWithWindows)
            try { StartupRegistration.Set(true); } catch { /* not important */ }

        if (settings.LastSerial != null) FileLog.AddSecret(settings.LastSerial);
        if (settings.LastWifiAddress != null) FileLog.AddSecret(settings.LastWifiAddress);
        Log($"MyDeX {UpdateChecker.CurrentVersion.ToString(3)} started.");
        LocateTools();
        if (settings.WifiPhoneSerial != null) FileLog.AddSecret(settings.WifiPhoneSerial);
        if (!string.IsNullOrEmpty(settings.LastWifiAddress) && await ConnectKnownWifiPhoneAsync(settings.LastWifiAddress))
            Log($"Reconnected to {settings.LastWifiAddress} over Wi-Fi.");
        await RefreshDevicesAsync();
        pollTimer.Start();
        batteryTimer.Start();
        await UpdateBatteryAsync();

        if (settings.CheckForUpdates && DateTime.Now - settings.LastUpdateCheck > TimeSpan.FromDays(1))
            await CheckForUpdatesAsync(quiet: true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyHotkey();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        GlobalHotkey.Unregister(Handle);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == GlobalHotkey.WM_HOTKEY && m.WParam == GlobalHotkey.Id)
            _ = ToggleDexAsync();
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            if (chkMinimizeToTray.Checked)
                HideToTray();
            else
                ExitApp();
            return;
        }

        exiting = true;
        SaveUiToSettings();
        pollTimer.Stop();
        batteryTimer.Stop();
        reconnectSerial = null;
        userStopping = true;
        // Run off the UI thread so the windows can close cleanly (finishing recordings) and adb cleanup can finish.
        var running = session;
        var windows = appWindows.ToList();
        var wifiPhones = settings.WifiOffOnExit ? lastDevices.Where(d => d.IsReady && d.Serial.Contains('.') && d.Serial.Contains(':')).ToList() : [];
        var adbAtExit = adb;
        Task.Run(async () =>
        {
            await CloseAppWindowsAsync(windows).ConfigureAwait(false);
            if (running != null)   // also cancels a start that is still in progress
                await running.StopAsync().ConfigureAwait(false);
            // Safety: don't leave the phone listening for adb on the network after MyDeX is gone.
            if (adbAtExit != null)
                foreach (var phone in wifiPhones)
                    await adbAtExit.UsbModeAsync(phone.Serial).ConfigureAwait(false);
        }).Wait(10_000);
        if (wifiPhones.Count > 0)
            Log("Switched Wi-Fi phones back to USB-only.");
        Log("MyDeX closed.");
        tray.Visible = false;
        tray.Dispose();
        base.OnFormClosing(e);
    }

    void HideToTray()
    {
        Hide();
        if (!trayTipShown)
        {
            trayTipShown = true;
            tray.ShowBalloonTip(3000, "MyDeX is still running",
                "MyDeX keeps running here. Right-click the icon to start/stop DeX or exit.", ToolTipIcon.Info);
        }
    }

    void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    void ExitApp()
    {
        exiting = true;
        Close();
    }

    void ShowLog()
    {
        if (logWindow == null || logWindow.IsDisposed)
        {
            logWindow = new Form
            {
                Text = "MyDeX log", Icon = Icon, Size = new Size(760, 480), StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false,
            };
            var open = new Button { Text = "Open log file", AutoSize = true, Dock = DockStyle.Bottom };
            open.Click += (_, _) => OpenUrl(FileLog.FilePath);
            logWindow.FormClosing += (_, e) =>
            {
                if (e.CloseReason != CloseReason.UserClosing) return;
                e.Cancel = true;   // keep txtLog alive; just hide
                logWindow.Hide();
            };
            logWindow.Controls.Add(txtLog);
            logWindow.Controls.Add(open);
        }
        logWindow.Show(this);
        logWindow.Activate();
    }

    // ───────────────────────── Devices ─────────────────────────

    void LocateTools()
    {
        var folder = ToolLocator.FindScrcpyFolder(txtScrcpyFolder.Text.Trim());
        scrcpyFolder = folder;
        if (folder == null)
        {
            adb = null;
            scrcpyExe = null;
            lblTools.Text = "scrcpy was not found. Download it (link below), unzip it, and pick that folder above " +
                            "(it must contain scrcpy.exe and adb.exe).";
            lblTools.ForeColor = Color.Firebrick;
        }
        else
        {
            scrcpyExe = Path.Combine(folder, "scrcpy.exe");
            adb = new AdbClient(Path.Combine(folder, "adb.exe"));   // always a full path; never looked up on PATH
            lblTools.Text = $"Using scrcpy from {folder}";
            lblTools.ForeColor = SystemColors.GrayText;
        }
        UpdateButtons();
    }

    async Task RefreshDevicesAsync()
    {
        if (adb == null || refreshing)
            return;
        refreshing = true;
        try
        {
            var devices = await adb.GetDevicesAsync();
            lastDevices = devices;
            foreach (var d in devices)
                FileLog.AddSecret(d.Serial);   // never written to the log file in full

            // Only rebuild the list when something changed, so an open dropdown doesn't close.
            string key = string.Join("|", devices.Select(d => d.Serial + d.State));
            bool devicesChanged = key != lastDeviceKey;
            if (devicesChanged)
            {
                lastDeviceKey = key;
                string? wanted = (cmbDevices.SelectedItem as PhoneDevice)?.Serial ?? settings.LastSerial;
                cmbDevices.BeginUpdate();
                cmbDevices.Items.Clear();
                foreach (var device in devices)
                    cmbDevices.Items.Add(device);
                var match = devices.FirstOrDefault(d => d.Serial == wanted) ?? devices.FirstOrDefault(d => d.IsReady) ?? devices.FirstOrDefault();
                cmbDevices.SelectedItem = match;
                cmbDevices.EndUpdate();
                Log(devices.Count == 0 ? "No phone connected." : "Phones: " + string.Join(", ", devices.Select(d => d.ToString().Trim())));
            }

            var ready = devices.Where(d => d.IsReady).ToList();

            // Remove a simulated display left behind by an unplug mid-session. Never while a start is in
            // progress: the display DeX is about to use is marked "pending" until scrcpy is running.
            if (settings.PendingOverlayCleanup is { } pending && session == null && !starting && ready.Any(d => d.Serial == pending))
            {
                var result = await adb.DeleteGlobalSettingAsync(pending, "overlay_display_devices");
                if (result.Ok)
                {
                    settings.PendingOverlayCleanup = null;
                    settings.Save();
                    Log("Removed a leftover extra display from the phone.");
                }
            }

            var newlyReady = ready.Where(d => !readySerials.Contains(d.Serial)).ToList();
            readySerials = ready.Select(d => d.Serial).ToHashSet();

            if (await TryReconnectAsync(ready))
                return;

            // Never start DeX by itself on a Wi-Fi device that hasn't been confirmed to be this user's phone.
            newlyReady = newlyReady.Where(d => !d.Serial.Contains(':') || verifiedWifi.Contains(d.Serial)).ToList();
            if (chkAutoStart.Checked && newlyReady.Count > 0 && session?.IsRunning != true && !starting && reconnectSerial == null)
            {
                var device = newlyReady.FirstOrDefault(d => d.Serial == settings.LastSerial) ?? newlyReady[0];
                SelectDevice(device.Serial);
                Log($"{device.Model} connected – starting DeX automatically.");
                await StartDexAsync(device);
            }
        }
        catch (Exception ex)
        {
            Log("Device check failed: " + ex.Message);
        }
        finally
        {
            refreshing = false;
            // Check every second while waiting for the phone to come back, every 3 seconds otherwise.
            pollTimer.Interval = reconnectSerial != null ? 1000 : 3000;
            UpdateButtons();
        }
    }

    /// <summary>Restarts DeX after the connection dropped, once the same phone is back and has settled.</summary>
    async Task<bool> TryReconnectAsync(List<PhoneDevice> ready)
    {
        if (reconnectSerial == null || adb == null || session?.IsRunning == true || starting)
            return false;

        var device = ready.FirstOrDefault(d => d.Serial == reconnectSerial);
        if (device == null)
        {
            reconnectSeenSince = DateTime.MaxValue;
            // A Wi-Fi phone won't come back on its own; knock every 10 seconds.
            if (reconnectSerial.Contains(':') && DateTime.Now - lastWifiRetry > TimeSpan.FromSeconds(10))
            {
                lastWifiRetry = DateTime.Now;
                await ConnectKnownWifiPhoneAsync(reconnectSerial);
            }
            return false;
        }

        // A re-plugged phone shows up in adb a moment before it is really usable; give it a second.
        if (reconnectSeenSince == DateTime.MaxValue)
        {
            reconnectSeenSince = DateTime.Now;
            Log($"{device.Model} is back – restarting DeX in a moment.");
            return false;
        }
        if (DateTime.Now - reconnectSeenSince < TimeSpan.FromSeconds(1))
            return false;

        if (reconnectAttempts >= MaxReconnectAttempts)
        {
            reconnectSerial = null;
            reconnectAttempts = 0;
            SetStatus("DeX could not be restarted after several tries. Click Start DeX to try again.");
            Log("Gave up reconnecting after several tries.");
            UpdateHome();
            return false;
        }
        reconnectAttempts++;
        reconnectSeenSince = DateTime.MaxValue;
        Log($"Restarting DeX on {device.Model} (attempt {reconnectAttempts} of {MaxReconnectAttempts}).");
        SelectDevice(device.Serial);
        await StartDexAsync(device, isReconnect: true);
        return true;
    }

    void SelectDevice(string serial) =>
        cmbDevices.SelectedItem = cmbDevices.Items.Cast<PhoneDevice>().FirstOrDefault(d => d.Serial == serial) ?? cmbDevices.SelectedItem;

    PhoneDevice? SelectedReadyDevice() => cmbDevices.SelectedItem is PhoneDevice { IsReady: true } d ? d : null;

    // ───────────────────────── DeX ─────────────────────────

    async Task ToggleDexAsync()
    {
        if (session?.IsRunning == true || reconnectSerial != null || starting)
            await StopDexAsync();
        else
            await StartSelectedAsync();
    }

    async Task StartSelectedAsync()
    {
        var device = SelectedReadyDevice();
        if (device == null)
        {
            SetStatus("No phone ready. Plug it in and allow USB debugging.");
            ShowFromTrayIfHidden();
            return;
        }
        await StartDexAsync(device);
    }

    async Task StartDexAsync(PhoneDevice device, bool isReconnect = false)
    {
        if (adb == null || scrcpyExe == null || starting || session?.IsRunning == true)
            return;

        starting = true;
        lastStartWasReconnect = isReconnect;
        if (!isReconnect)
            reconnectAttempts = 0;   // a fresh start gets the full number of reconnect tries
        UpdateButtons();
        SaveUiToSettings();
        settings.LastSerial = device.Serial;
        if (device.Serial.Contains(':') && verifiedWifi.Contains(device.Serial))
            settings.LastWifiAddress = device.Serial;
        settings.Save();
        SetStatus($"Starting DeX on {device.Model}...");

        var newSession = new DexSession(adb, scrcpyExe, settings);
        newSession.Log += message => OnUi(() => Log(message));
        newSession.Ended += code => OnUi(() => OnSessionEnded(newSession, device, code));
        session = newSession;
        sessionDevice = device;
        userStopping = false;

        try
        {
            await newSession.StartAsync(device, soundElsewhere: appWindows.Any(w => w.IsRunning && w.HasSound));
            sessionStartedAt = DateTime.Now;
            SetStatus($"DeX is running on {device.Model}.");
        }
        catch (OperationCanceledException)
        {
            // Stop was clicked (or MyDeX closed) while DeX was still starting.
            session = null;
            sessionDevice = null;
            userStopping = false;
            reconnectSerial = null;
            SetStatus("DeX stopped.");
        }
        catch (Exception ex)
        {
            session = null;
            sessionDevice = null;
            Log("Could not start DeX: " + ex.Message);
            SetStatus("Could not start DeX: " + ex.Message);
            if (!Visible)
                tray.ShowBalloonTip(4000, "MyDeX", "Could not start DeX: " + ex.Message, ToolTipIcon.Warning);
            // During a reconnect keep waiting; TryReconnectAsync counts the attempts.
        }
        finally
        {
            starting = false;
            UpdateButtons();
            UpdateTrayText();
        }
    }

    void OnSessionEnded(DexSession ended, PhoneDevice device, int exitCode)
    {
        if (session != ended)
            return;
        session = null;
        sessionDevice = null;

        if (ended.RecordingPath is { } recording && File.Exists(recording))
            Log($"Recording saved: {recording}");

        var ranFor = DateTime.Now - sessionStartedAt;
        if (ranFor > TimeSpan.FromMinutes(1))
            reconnectAttempts = 0;

        bool connectionProblem = Scrcpy.LooksLikeConnectionLoss(exitCode, ranFor, lastStartWasReconnect);
        bool canRetry = reconnectAttempts < MaxReconnectAttempts;
        if (!userStopping && !exiting && chkAutoReconnect.Checked && connectionProblem && !canRetry)
            Log($"Not reconnecting: already tried {MaxReconnectAttempts} times in a row.");
        if (!userStopping && !exiting && chkAutoReconnect.Checked && connectionProblem && canRetry)
        {
            reconnectSerial = device.Serial;
            reconnectSeenSince = DateTime.MaxValue;
            pollTimer.Interval = 1000;   // look for the phone every second until it is back
            Log($"Connection to {device.Model} lost (exit code {exitCode}) – waiting for it to come back.");
            SetStatus($"Lost the connection to {device.Model}. DeX comes back when it reconnects.");
            if (!Visible)
                tray.ShowBalloonTip(3000, "MyDeX", "Connection lost – DeX will come back when the phone reconnects.", ToolTipIcon.Info);
        }
        else
        {
            if (!userStopping && connectionProblem && !chkAutoReconnect.Checked)
                Log("The phone disconnected. (Auto-reconnect is off in Settings.)");
            reconnectSerial = null;
            SetStatus(exitCode == 0 || userStopping ? "DeX stopped." : $"DeX stopped unexpectedly (scrcpy exit code {exitCode}). Settings › Show log has details.");
        }
        userStopping = false;
        UpdateButtons();
        UpdateTrayText();
    }

    async Task StopDexAsync()
    {
        reconnectSerial = null;
        reconnectAttempts = 0;
        if (session == null)
        {
            SetStatus("DeX stopped.");
            UpdateButtons();
            return;
        }
        userStopping = true;
        SetStatus("Stopping DeX...");
        btnToggle.Enabled = false;
        await session.StopAsync();
    }

    // ───────────────────────── Helpers ─────────────────────────

    void UpdateButtons()
    {
        bool running = session?.IsRunning == true;
        bool canStart = adb != null && scrcpyExe != null && !starting && !running && SelectedReadyDevice() != null;
        miStart.Enabled = canStart;
        miStop.Enabled = running || starting || reconnectSerial != null;
        UpdateHome();
    }

    void SetStatus(string text) => lblStatus.Text = text;

    void Log(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (txtLog.TextLength > 200_000)
            txtLog.Clear();
        txtLog.AppendText(line + Environment.NewLine);
        FileLog.Write(line);
    }

    void OnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { /* closing */ }
    }

    void ShowFromTrayIfHidden()
    {
        if (!Visible) ShowFromTray();
    }

    static void SetBusy(Button button, bool busy)
    {
        button.Enabled = !busy;
        button.Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* no browser */ }
    }
}

/// <summary>The coloured circle on the Home status card.</summary>
sealed class StatusDot : Control
{
    public static readonly Color Good = Color.FromArgb(40, 170, 80);
    public static readonly Color Ready = Color.FromArgb(22, 110, 230);
    public static readonly Color Busy = Color.FromArgb(235, 160, 20);
    public static readonly Color Bad = Color.FromArgb(200, 60, 60);

    Color color = Bad;

    public StatusDot()
    {
        Size = new Size(26, 26);
        Margin = new Padding(0, 6, 12, 0);
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
    }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Color
    {
        get => color;
        set { color = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        e.Graphics.FillEllipse(brush, 1, 1, Width - 3, Height - 3);
    }
}
