namespace MyDeX;

/// <summary>Apps, Wi-Fi, files, battery/heat, hotkey, updates and the tray menu.</summary>
public sealed partial class MainForm
{
    BatteryInfo? battery;
    bool batteryBusy, heatWarned, hotkeyRegistered, pushing, loadingApps, openingApp;
    ReleaseInfo? scrcpyUpdate;
    /// <summary>Wi-Fi addresses confirmed (by hardware serial) to be the user's own phone this session.</summary>
    readonly HashSet<string> verifiedWifi = [];

    /// <summary>
    /// Connects to a remembered Wi-Fi address, but keeps the connection only if the device there is the
    /// same phone (same hardware serial). On another network – a hotel, a café – the same address can
    /// belong to someone else's device, which must never get the PC's clipboard, files or input.
    /// </summary>
    async Task<bool> ConnectKnownWifiPhoneAsync(string address)
    {
        if (adb == null || settings.WifiPhoneSerial == null) return false;
        var (ok, _) = await adb.ConnectAsync(address);
        if (!ok) return false;
        if (await adb.GetHardwareSerialAsync(address) == settings.WifiPhoneSerial)
        {
            verifiedWifi.Add(address);
            return true;
        }
        await adb.DisconnectAsync(address);
        Log($"The device at {address} is not your phone, so MyDeX disconnected it (are you on another network?).");
        return false;
    }

    /// <summary>Remembers a Wi-Fi phone the user connected on purpose, and which phone it is.</summary>
    async Task RememberWifiPhoneAsync(string address, string? hardwareSerial)
    {
        hardwareSerial ??= await adb!.GetHardwareSerialAsync(address);
        settings.LastWifiAddress = address;
        settings.WifiPhoneSerial = hardwareSerial;
        if (hardwareSerial != null) FileLog.AddSecret(hardwareSerial);
        FileLog.AddSecret(address);
        verifiedWifi.Add(address);
        settings.Save();
    }

    /// <summary>A row in the Apps tab's "Open windows" list.</summary>
    sealed record AppWindowItem(AppWindow Window)
    {
        public override string ToString() => Window.App.Name + (Window.HasSound ? "   🔊" : "");
    }

    // ───────────────────────── Tray ─────────────────────────

    void SetupTray()
    {
        miStart.Click += async (_, _) => await StartSelectedAsync();
        miStop.Click += async (_, _) => await StopDexAsync();

        var menu = new ContextMenuStrip();
        menu.Items.Add(miStart);
        menu.Items.Add(miStop);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(miProfiles);
        menu.Items.Add(miRecentApps);
        menu.Items.Add("Show my phone screen", null, async (_, _) => await OpenAppAsync(AppWindow.PhoneScreen));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open MyDeX", null, (_, _) => ShowFromTray());
        menu.Items.Add("Show log", null, (_, _) => { ShowFromTray(); ShowLog(); });
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        menu.Opening += (_, _) => BuildTrayMenus();

        tray.ContextMenuStrip = menu;
        tray.Icon = Icon;
        tray.Text = "MyDeX";
        tray.Visible = true;
        tray.DoubleClick += (_, _) => ShowFromTray();
        BuildTrayMenus();
    }

    /// <summary>Rebuilt every time the menu opens, so it always shows the current modes and apps.</summary>
    void BuildTrayMenus()
    {
        miProfiles.DropDownItems.Clear();
        foreach (var profile in settings.Profiles)
        {
            string name = profile.Name;
            var item = new ToolStripMenuItem(name) { Checked = name == settings.Profile.Name };
            item.Click += (_, _) =>
            {
                if (name != settings.Profile.Name)
                    SwitchProfile(name);
            };
            miProfiles.DropDownItems.Add(item);
        }

        miRecentApps.DropDownItems.Clear();
        var apps = settings.PinnedApps.Concat(settings.RecentApps).DistinctBy(a => a.Package).Take(10).ToList();
        if (apps.Count == 0)
        {
            miRecentApps.DropDownItems.Add(new ToolStripMenuItem("(pick apps on the Apps tab first)") { Enabled = false });
        }
        else
        {
            foreach (var app in apps)
            {
                var item = new ToolStripMenuItem((settings.IsPinned(app) ? "★ " : "") + app.Name);
                item.Click += async (_, _) => await OpenAppAsync(app);
                miRecentApps.DropDownItems.Add(item);
            }
        }
    }

    void UpdateTrayText()
    {
        if (exiting) return;
        string text = session?.IsRunning == true && sessionDevice != null ? $"MyDeX – DeX on {sessionDevice.Model}"
            : reconnectSerial != null ? "MyDeX – waiting for the phone to reconnect"
            : "MyDeX";
        if (battery != null)
            text += $"\n{battery.Percent}%{(battery.Charging ? " (charging)" : "")} · {battery.TemperatureC:0.0} °C";
        tray.Text = text.Length > 127 ? text[..127] : text;   // Windows limit; longer throws
    }

    // ───────────────────────── Hotkey ─────────────────────────

    void ApplyHotkey()
    {
        if (!IsHandleCreated) return;
        if (hotkeyRegistered)
        {
            GlobalHotkey.Unregister(Handle);
            hotkeyRegistered = false;
        }
        if (settings.HotkeyEnabled)
        {
            hotkeyRegistered = GlobalHotkey.Register(Handle);
            if (!hotkeyRegistered)
                Log($"{GlobalHotkey.Description} is already used by another program, so the MyDeX shortcut is off.");
        }
    }

    // ───────────────────────── Battery & heat ─────────────────────────

    async Task UpdateBatteryAsync()
    {
        if (adb == null || batteryBusy) return;
        var device = sessionDevice ?? SelectedReadyDevice();
        if (device == null)
        {
            battery = null;
            lblBattery.Text = "";
            UpdateTrayText();
            UpdateHome();
            return;
        }

        batteryBusy = true;
        try
        {
            battery = await adb.GetBatteryAsync(device.Serial);
            if (battery == null)
            {
                lblBattery.Text = "";
                return;
            }
            lblBattery.Text = $"Battery {battery.Percent}%{(battery.Charging ? " (charging)" : "")} · {battery.TemperatureC:0.0} °C";
            bool hot = battery.TemperatureC >= settings.HeatWarningC;
            lblBattery.ForeColor = hot ? Color.Firebrick : SystemColors.ControlText;
            if (hot && !heatWarned)
            {
                heatWarned = true;
                string message = $"{device.Model} is at {battery.TemperatureC:0.0} °C. Lower the smoothness (FPS) or quality, or give it a break.";
                Log("Heat warning: " + message);
                tray.ShowBalloonTip(6000, "Your phone is getting hot", message, ToolTipIcon.Warning);
            }
            else if (battery.TemperatureC < settings.HeatWarningC - 2)
            {
                heatWarned = false;   // warn again only after it has cooled down a bit
            }
        }
        catch (Exception ex)
        {
            Log("Battery check failed: " + ex.Message);
        }
        finally
        {
            batteryBusy = false;
            UpdateTrayText();
            UpdateHome();
        }
    }

    // ───────────────────────── Wi-Fi ─────────────────────────

    async Task SwitchToWifiAsync()
    {
        if (adb == null) return;
        var device = SelectedReadyDevice();
        if (device == null || device.IsWifi)
        {
            MessageBox.Show(this, "Plug the phone in with a USB cable first.", "MyDeX");
            return;
        }
        if (session?.IsRunning == true || appWindows.Count > 0)
        {
            MessageBox.Show(this, "Stop DeX and close the app windows first – switching restarts the phone's connection.", "MyDeX");
            return;
        }

        SetBusy(btnToWifi, true);
        try
        {
            string? ip = await adb.GetWifiIpAsync(device.Serial);
            if (ip == null)
            {
                MessageBox.Show(this, "The phone doesn't seem to be on Wi-Fi. Connect it to the same network as this PC.", "MyDeX");
                return;
            }
            Log($"Phone IP is {ip}. Enabling Wi-Fi debugging...");
            var tcpip = await adb.TcpipAsync(device.Serial, 5555);
            Log(tcpip.Output);
            await Task.Delay(2500);
            string address = $"{ip}:5555";
            var (ok, message) = await adb.ConnectAsync(address);
            Log(message);
            if (ok)
            {
                await RememberWifiPhoneAsync(address, device.Serial);   // over USB the name is the hardware serial
                txtConnect.Text = address;
                SetStatus($"Connected over Wi-Fi ({address}). You can unplug the cable now.");
                MessageBox.Show(this, "Done! You can unplug the cable now – DeX works over Wi-Fi.", "MyDeX");
            }
            else
            {
                SetStatus("Wi-Fi connection failed. Check that the PC and phone are on the same network.");
            }
            lastDeviceKey = "";
            await RefreshDevicesAsync();
        }
        finally
        {
            SetBusy(btnToWifi, false);
        }
    }

    async Task ConnectWifiAsync(string address)
    {
        if (adb == null) return;
        address = address.Trim();
        if (address.Length == 0) return;
        if (!address.Contains(':')) address += ":5555";

        SetBusy(btnConnect, true);
        try
        {
            var (ok, message) = await adb.ConnectAsync(address);
            Log(message);
            if (ok)
            {
                await RememberWifiPhoneAsync(address, null);
                SetStatus($"Connected to {address}.");
            }
            else
            {
                SetStatus($"Could not connect to {address}.");
            }
            lastDeviceKey = "";
            await RefreshDevicesAsync();
        }
        finally
        {
            SetBusy(btnConnect, false);
        }
    }

    async Task PairAsync()
    {
        if (adb == null) return;
        string address = txtPairAddress.Text.Trim(), code = txtPairCode.Text.Trim();
        if (address.Length == 0 || code.Length == 0)
        {
            MessageBox.Show(this, "Enter the pairing address and code shown on the phone.", "MyDeX");
            return;
        }
        SetBusy(btnPair, true);
        try
        {
            var (ok, message) = await adb.PairAsync(address, code);
            Log(message);
            SetStatus(ok
                ? "Paired! Now enter the IP address & port from the Wireless debugging screen and click Connect."
                : "Pairing failed. Check the code and address and try again.");
            if (ok)
                txtConnect.Text = address.Split(':')[0] + ":";
        }
        finally
        {
            SetBusy(btnPair, false);
        }
    }

    async Task DisconnectWifiAsync()
    {
        if (adb == null) return;
        if (session?.IsRunning == true && sessionDevice?.IsWifi == true)
            await StopDexAsync();
        reconnectSerial = null;
        // "adb tcpip" leaves the phone listening for adb on the network until it reboots; switch it
        // back to USB-only so nobody else on the network can try to connect to it.
        foreach (var phone in lastDevices.Where(d => d.IsReady && d.Serial.Contains('.') && d.Serial.Contains(':')))
        {
            var usb = await adb.UsbModeAsync(phone.Serial);
            Log($"{phone.Model}: Wi-Fi debugging turned off ({usb.Output}).");
        }
        var result = await adb.RunAsync(["disconnect"]);
        Log(result.Output);
        settings.LastWifiAddress = null;
        settings.WifiPhoneSerial = null;
        verifiedWifi.Clear();
        settings.Save();
        lastDeviceKey = "";
        await RefreshDevicesAsync();
    }

    // ───────────────────────── App windows ─────────────────────────

    async Task LoadAppsAsync()
    {
        if (adb == null || scrcpyExe == null || loadingApps) return;
        var device = SelectedReadyDevice();
        if (device == null)
        {
            SetStatus("Connect your phone to see its apps.");
            return;
        }

        loadingApps = true;
        SetBusy(btnLoadApps, true);
        lvApps.Items.Clear();
        lvApps.Items.Add(new ListViewItem("Loading your apps... (a few seconds)") { ForeColor = SystemColors.GrayText });
        try
        {
            phoneApps = await Scrcpy.ListAppsAsync(scrcpyExe, adb.AdbPath, device.Serial);
            appsSerial = device.Serial;
            Log($"Found {phoneApps.Count} apps on {device.Model}.");
            FilterApps();
            if (phoneApps.Count == 0)
                lvApps.Items.Add(new ListViewItem("Couldn't read the app list – try Reload list.") { ForeColor = Color.Firebrick });
        }
        finally
        {
            loadingApps = false;
            SetBusy(btnLoadApps, false);
        }
    }

    void FilterApps()
    {
        string query = txtAppSearch.Text.Trim();
        string? selected = (lvApps.SelectedItems.Count > 0 ? lvApps.SelectedItems[0].Tag as PhoneApp : null)?.Package;
        lvApps.BeginUpdate();
        lvApps.Items.Clear();
        // Pinned apps first, then alphabetical.
        foreach (var app in phoneApps.OrderByDescending(settings.IsPinned))
        {
            if (query.Length > 0
                && !app.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                && !app.Package.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            var item = new ListViewItem((settings.IsPinned(app) ? "★  " : "     ") + app.Name) { Tag = app, ToolTipText = app.Package };
            lvApps.Items.Add(item);
            if (app.Package == selected)
                item.Selected = true;
        }
        if (lvApps.SelectedItems.Count == 0 && lvApps.Items.Count > 0)
            lvApps.Items[0].Selected = true;
        lvApps.EndUpdate();
        lvApps.Columns[0].Width = -2;   // fill the width once the vertical scrollbar is known (no horizontal bar)
        UpdatePinButton();
    }

    PhoneApp? SelectedApp() => lvApps.SelectedItems.Count > 0 ? lvApps.SelectedItems[0].Tag as PhoneApp : null;

    void UpdatePinButton()
    {
        var app = SelectedApp();
        btnPinApp.Enabled = app != null;
        btnOpenApp.Enabled = app != null;
        btnPinApp.Text = app != null && settings.IsPinned(app) ? "☆  Remove from Home" : "★  Add to Home";
    }

    void TogglePinSelected()
    {
        if (SelectedApp() is not { } app) return;
        bool pinned = settings.TogglePinned(app);
        settings.Save();
        SetStatus(pinned ? $"{app.Name} is on the Home tab now." : $"{app.Name} removed from the Home tab.");
        BuildAppTiles();
        FilterApps();
    }

    async Task OpenSelectedAppAsync()
    {
        if (phoneApps.Count == 0 || loadingApps)
        {
            await LoadAppsAsync();
            return;
        }
        if (SelectedApp() is { } app)
            await OpenAppAsync(app);
    }

    /// <summary>Opens an app – or the phone's own screen – in a new window next to DeX.</summary>
    async Task OpenAppAsync(PhoneApp app)
    {
        if (adb == null || scrcpyExe == null || openingApp) return;   // ignore double-clicks while one is opening
        var device = SelectedReadyDevice();
        if (device == null)
        {
            SetStatus("Connect your phone first.");
            ShowFromTrayIfHidden();
            return;
        }
        if (app.Package.Length == 0 && appWindows.Any(w => w.IsPhoneScreen && w.IsRunning))
        {
            SetStatus("Your phone screen is already open in a window.");
            return;
        }

        openingApp = true;
        try
        {
            await OpenAppCoreAsync(app, device);
        }
        finally
        {
            openingApp = false;
        }
    }

    async Task OpenAppCoreAsync(PhoneApp app, PhoneDevice device)
    {
        SaveUiToSettings();
        await adb!.ShellAsync(device.Serial, "mkdir", "-p", Scrcpy.PushTarget);
        bool soundElsewhere = session is { IsRunning: true, HasSound: true } || appWindows.Any(w => w.IsRunning && w.HasSound);
        var window = new AppWindow(adb, scrcpyExe!, settings, app);
        window.Log += message => OnUi(() => Log(message));
        window.Ended += () => OnUi(() =>
        {
            appWindows.Remove(window);
            UpdateAppWindowsList();
        });
        try
        {
            window.Start(device, soundElsewhere, gamepadElsewhere: appWindows.Any(w => w.IsRunning && w.HasGamepad));
            appWindows.Add(window);
            settings.AddRecentApp(app);
            settings.Save();
            if (settings.PinnedApps.Count == 0)
                BuildAppTiles();   // Home shows recent apps until something is pinned
            SetStatus(app.Package.Length == 0 ? "Your phone screen is open in a window." : $"Opened {app.Name} in its own window.");
        }
        catch (Exception ex)
        {
            Log(ex.Message);
            SetStatus(ex.Message);
        }
        UpdateAppWindowsList();
    }

    Task CloseAllAppWindowsAsync() => CloseAppWindowsAsync(appWindows.ToList());

    /// <summary>Safe to call off the UI thread: works on a snapshot of the windows.</summary>
    static Task CloseAppWindowsAsync(IEnumerable<AppWindow> windows) => Task.WhenAll(windows.Select(w => w.StopAsync()));

    void UpdateAppWindowsList()
    {
        lstOpenWindows.BeginUpdate();
        lstOpenWindows.Items.Clear();
        foreach (var window in appWindows)
            lstOpenWindows.Items.Add(new AppWindowItem(window));
        if (lstOpenWindows.Items.Count > 0)
            lstOpenWindows.SelectedIndex = lstOpenWindows.Items.Count - 1;
        lstOpenWindows.EndUpdate();
        btnCloseWindow.Enabled = btnCloseApps.Enabled = appWindows.Count > 0;
    }

    // ───────────────────────── Files ─────────────────────────

    /// <summary>Lets files be dropped anywhere on the window (including controls added later).</summary>
    void EnableFileDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) =>
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        control.DragDrop += async (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
                await PushFilesAsync(paths);
        };
        control.ControlAdded += (_, e) => { if (e.Control != null) EnableFileDrop(e.Control); };
        foreach (Control child in control.Controls)
            EnableFileDrop(child);
    }

    async Task PushFilesAsync(string[] paths)
    {
        if (adb == null || pushing) return;
        var device = SelectedReadyDevice();
        if (device == null)
        {
            // No MessageBox here: this runs inside the drop, and a dialog would freeze Explorer until closed.
            SetStatus("Connect your phone first, then drop the files again.");
            return;
        }

        pushing = true;
        try
        {
            await adb.ShellAsync(device.Serial, "mkdir", "-p", Scrcpy.PushTarget);
            int sent = 0;
            for (int i = 0; i < paths.Length; i++)
            {
                string name = Path.GetFileName(paths[i].TrimEnd('\\'));
                SetStatus($"Sending {name} to the phone ({i + 1}/{paths.Length})...");
                var result = await adb.PushAsync(device.Serial, paths[i], Scrcpy.PushTarget);
                Log(result.Ok ? $"Sent {name}" : $"Failed to send {name}: {result.Output}");
                if (result.Ok) sent++;
            }
            SetStatus($"Sent {sent} of {paths.Length} to the phone (Download › MyDeX).");
        }
        finally
        {
            pushing = false;
        }
    }

    // ───────────────────────── Updates ─────────────────────────

    async Task CheckForUpdatesAsync(bool quiet)
    {
        SetBusy(btnCheckUpdates, true);
        lblUpdates.Text = "Checking for updates...";
        try
        {
            var notes = new List<string>();
            var current = UpdateChecker.CurrentVersion;
            var mine = await UpdateChecker.GetLatestMyDexAsync();
            if (UpdateChecker.IsNewer(mine?.Version, current))
            {
                notes.Add($"MyDeX {mine!.Tag} is available (you have {current.ToString(3)}).");
                lnkMyDexUpdate.Text = $"Download MyDeX {mine.Tag}";
                lnkMyDexUpdate.Tag = mine.PageUrl;
                lnkMyDexUpdate.Visible = true;
            }
            else
            {
                notes.Add(mine == null
                    ? $"MyDeX {current.ToString(3)} – couldn't see any published releases."
                    : $"MyDeX {current.ToString(3)} is up to date.");
                lnkMyDexUpdate.Visible = false;
            }

            scrcpyUpdate = null;
            if (scrcpyExe != null)
            {
                var have = await Scrcpy.GetVersionAsync(scrcpyExe);
                var latest = await UpdateChecker.GetLatestScrcpyAsync();
                if (latest == null)
                    notes.Add("Couldn't reach GitHub to check scrcpy.");
                else if (UpdateChecker.IsNewer(latest.Version, have))
                {
                    scrcpyUpdate = latest;
                    notes.Add($"scrcpy {latest.Tag} is available (you have {have?.ToString(2) ?? "an unknown version"}).");
                }
                else
                    notes.Add($"scrcpy {have?.ToString(2)} is up to date.");
            }

            btnUpdateScrcpy.Visible = scrcpyUpdate != null;
            btnUpdateScrcpy.Text = $"Update scrcpy to {scrcpyUpdate?.Tag}";
            lblUpdates.Text = string.Join(Environment.NewLine, notes);
            settings.LastUpdateCheck = DateTime.Now;
            settings.Save();

            if (quiet && (lnkMyDexUpdate.Visible || scrcpyUpdate != null))
                tray.ShowBalloonTip(5000, "MyDeX: update available", string.Join(" ", notes.Where(n => n.Contains("available"))), ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            lblUpdates.Text = "Update check failed: " + ex.Message;
        }
        finally
        {
            SetBusy(btnCheckUpdates, false);
        }
    }

    async Task UpdateScrcpyAsync()
    {
        if (scrcpyUpdate == null || adb == null || scrcpyFolder == null) return;
        if (session?.IsRunning == true || appWindows.Count > 0)
        {
            MessageBox.Show(this, "Stop DeX and close all app windows first.", "MyDeX");
            return;
        }
        if (!IsBundledFolder(scrcpyFolder))
        {
            MessageBox.Show(this, $"MyDeX only updates the copy of scrcpy that ships with it.\n\nYours is in {scrcpyFolder} – update it there.", "MyDeX");
            return;
        }

        SetBusy(btnUpdateScrcpy, true);
        pollTimer.Stop();
        batteryTimer.Stop();
        try
        {
            // adb.exe lives in the folder being replaced, so its background server has to stop first.
            await adb.KillServerAsync();
            await Task.Delay(1000);
            await UpdateChecker.UpdateScrcpyAsync(scrcpyUpdate, scrcpyFolder, message => OnUi(() => Log(message)));
            lblUpdates.Text = $"scrcpy {scrcpyUpdate.Tag} installed.";
            scrcpyUpdate = null;
            btnUpdateScrcpy.Visible = false;
            LocateTools();
            lastDeviceKey = "";
        }
        catch (Exception ex)
        {
            Log("scrcpy update failed: " + ex.Message);
            MessageBox.Show(this, "The scrcpy update failed: " + ex.Message, "MyDeX");
        }
        finally
        {
            SetBusy(btnUpdateScrcpy, false);
            pollTimer.Start();
            batteryTimer.Start();
            await RefreshDevicesAsync();
        }
    }

    static bool IsBundledFolder(string folder) =>
        Path.GetFullPath(folder).StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase);
}
