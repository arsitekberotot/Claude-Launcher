using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

// ---------------------------------------------------------------- settings & presets

class AppSettings
{
    public bool AutoStart, ArrangeOnStart, CloseToTray = true, Hotkeys = true;
    static readonly string FilePath = Path.Combine(Store.Root, "settings.txt");
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "ClaudeLauncher";

    public static AppSettings Load()
    {
        var s = new AppSettings();
        if (!File.Exists(FilePath)) return s;
        foreach (var line in File.ReadAllLines(FilePath))
        {
            var i = line.IndexOf('=');
            if (i <= 0) continue;
            var key = line.Substring(0, i).Trim();
            var on = line.Substring(i + 1).Trim() == "1";
            if (key == "autostart") s.AutoStart = on;
            else if (key == "arrangeOnStart") s.ArrangeOnStart = on;
            else if (key == "closeToTray") s.CloseToTray = on;
            else if (key == "hotkeys") s.Hotkeys = on;
        }
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(Store.Root);
        File.WriteAllLines(FilePath, new[]
        {
            "autostart=" + (AutoStart ? "1" : "0"),
            "arrangeOnStart=" + (ArrangeOnStart ? "1" : "0"),
            "closeToTray=" + (CloseToTray ? "1" : "0"),
            "hotkeys=" + (Hotkeys ? "1" : "0")
        });
    }

    // Per-user "Run" entry; started with --autostart so the launcher opens in the tray.
    public static void ApplyAutoStart(bool on)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (on) key.SetValue(RunName, "\"" + Application.ExecutablePath + "\" --autostart");
            else key.DeleteValue(RunName, false);
        }
    }
}

class Preset
{
    public string Name, Layout, Monitor;
    public bool Gap;
    public List<string> Ids = new List<string>();
}

static class PresetStore
{
    static readonly string FilePath = Path.Combine(Store.Root, "presets.txt");

    // presets.txt: name \t layout \t monitor \t gap(1/0) \t id,id,...
    public static List<Preset> Load()
    {
        var list = new List<Preset>();
        if (!File.Exists(FilePath)) return list;
        foreach (var line in File.ReadAllLines(FilePath))
        {
            var parts = line.Split('\t');
            if (parts.Length < 5 || parts[0].Length == 0) continue;
            list.Add(new Preset
            {
                Name = parts[0],
                Layout = parts[1],
                Monitor = parts[2],
                Gap = parts[3] == "1",
                Ids = parts[4].Split(',').Where(x => x.Length > 0).ToList()
            });
        }
        return list;
    }

    public static void Save(List<Preset> list)
    {
        Directory.CreateDirectory(Store.Root);
        File.WriteAllLines(FilePath, list.Select(p => string.Join("\t", new[]
        {
            p.Name.Replace("\t", " "), p.Layout, p.Monitor, p.Gap ? "1" : "0", string.Join(",", p.Ids)
        })));
    }
}

static class HotkeyNative
{
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
    public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string name);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // Sent by a second launcher process so the running one shows itself, even from the tray.
    public static readonly int ShowMessage = RegisterWindowMessage("ClaudeLauncher.Show");
}

// ---------------------------------------------------------------- launcher features

partial class LauncherForm
{
    const int HotkeyArrange = 100; // Ctrl+Alt+0; Ctrl+Alt+1..9 use ids 1..9
    readonly AppSettings settings = AppSettings.Load();
    List<Preset> presets = PresetStore.Load();
    NotifyIcon tray;
    bool exiting, hotkeysRegistered, hideTipShown;

    void InitFeatures(bool autostart)
    {
        tray = new NotifyIcon { Icon = Icon, Text = "Claude Launcher", Visible = true };
        var trayMenu = new ContextMenuStrip { Renderer = renderer, Font = Theme.Body, ShowCheckMargin = false, ShowImageMargin = false };
        trayMenu.Opening += (s, e) => { e.Cancel = false; FillTrayMenu(trayMenu); };
        tray.ContextMenuStrip = trayMenu;
        tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };

        if (autostart && settings.ArrangeOnStart)
        {
            var t = new System.Windows.Forms.Timer { Interval = 4000 };
            t.Tick += (s, e) =>
            {
                t.Stop();
                t.Dispose();
                OpenAndArrange();
            };
            t.Start();
        }
    }

    public bool StartHidden { get { return settings.CloseToTray; } }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    void ExitApp()
    {
        exiting = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && settings.CloseToTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!hideTipShown)
            {
                hideTipShown = true;
                tray.ShowBalloonTip(3000, "Claude Launcher masih jalan", "Launcher ada di system tray. Klik ikonnya untuk membuka lagi.", ToolTipIcon.None);
            }
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        UnregisterHotkeys();
        if (tray != null)
        {
            tray.Visible = false;
            tray.Dispose();
        }
        base.OnFormClosed(e);
        Application.Exit();
    }

    // ---------- hotkeys ----------

    void RegisterHotkeys()
    {
        if (!settings.Hotkeys || hotkeysRegistered || !IsHandleCreated) return;
        uint mods = HotkeyNative.MOD_CONTROL | HotkeyNative.MOD_ALT | HotkeyNative.MOD_NOREPEAT;
        for (int i = 1; i <= 9; i++) HotkeyNative.RegisterHotKey(Handle, i, mods, (uint)('0' + i));
        HotkeyNative.RegisterHotKey(Handle, HotkeyArrange, mods, (uint)'0');
        hotkeysRegistered = true;
    }

    void UnregisterHotkeys()
    {
        if (!hotkeysRegistered || !IsHandleCreated) return;
        for (int i = 1; i <= 9; i++) HotkeyNative.UnregisterHotKey(Handle, i);
        HotkeyNative.UnregisterHotKey(Handle, HotkeyArrange);
        hotkeysRegistered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == HotkeyNative.WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            if (id == HotkeyArrange) OpenAndArrange();
            else if (id >= 1 && id <= profiles.Count) Launch(profiles[id - 1]);
            return;
        }
        if (m.Msg == HotkeyNative.ShowMessage && HotkeyNative.ShowMessage != 0)
        {
            ShowFromTray();
            return;
        }
        base.WndProc(ref m);
    }

    // ---------- close instances ----------

    static void Kill(int pid)
    {
        try
        {
            var psi = new ProcessStartInfo("taskkill", "/PID " + pid + " /T /F") { CreateNoWindow = true, UseShellExecute = false };
            using (var p = Process.Start(psi)) p.WaitForExit(5000);
        }
        catch { }
    }

    void CloseInstance(Profile p)
    {
        int pid;
        if (!Claude.RunningProcesses().TryGetValue(Claude.Key(p), out pid))
        {
            Poll();
            return;
        }
        if (!DarkDialog.Confirm(this, "Tutup instance", "Tutup \"" + p.Name + "\"? Claude di instance ini akan dihentikan.", "Tutup", true)) return;
        Kill(pid);
        PollSoon();
    }

    void CloseAll()
    {
        var procs = Claude.RunningProcesses();
        var targets = profiles.Where(p => procs.ContainsKey(Claude.Key(p))).ToList();
        if (targets.Count == 0)
        {
            DarkDialog.Alert(this, "Tidak ada yang berjalan", "Belum ada instance yang sedang berjalan.");
            return;
        }
        if (!DarkDialog.Confirm(this, "Tutup semua instance",
            "Tutup " + targets.Count + " instance yang sedang berjalan?\n\n" + string.Join("\n", targets.Select(t => "• " + t.Name)),
            "Tutup semua", true)) return;
        foreach (var t in targets) Kill(procs[Claude.Key(t)]);
        PollSoon();
    }

    // ---------- presets ----------

    void RunPreset(Preset preset)
    {
        layout.Layout = preset.Layout;
        layout.Monitor = preset.Monitor;
        layout.Gap = preset.Gap;
        layout.Ids = profiles.Where(p => preset.Ids.Contains(p.Id, StringComparer.OrdinalIgnoreCase)).Select(p => p.Id).ToList();
        layout.Save();
        BuildCards();
        OpenAndArrange();
    }

    void SavePreset()
    {
        var active = profiles.Where(p => layout.Includes(p)).ToList();
        var name = DarkDialog.Prompt(this, "Simpan preset",
            "Simpan tata letak sekarang sebagai preset: " + active.Count + " instance aktif (" +
            string.Join(", ", active.Select(p => p.Name)) + "), layout, monitor, dan jarak.\nNama yang sama akan menimpa preset lama.",
            "", "Simpan", null, ValidateName);
        if (name == null) return;
        presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        presets.Add(new Preset { Name = name, Layout = layout.Layout, Monitor = layout.Monitor, Gap = layout.Gap, Ids = active.Select(p => p.Id).ToList() });
        PresetStore.Save(presets);
    }

    void DeletePreset(Preset preset)
    {
        if (!DarkDialog.Confirm(this, "Hapus preset", "Hapus preset \"" + preset.Name + "\"?", "Hapus", true)) return;
        presets.Remove(preset);
        PresetStore.Save(presets);
    }

    string PresetLabel(Preset p)
    {
        return p.Name + "  (" + p.Ids.Count + " instance)";
    }

    // ---------- menus ----------

    ToolStripMenuItem Check(string text, bool on, Action<bool> set)
    {
        var item = new ToolStripMenuItem(text) { Checked = on, Padding = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
        item.Click += (s, e) => set(!on);
        return item;
    }

    static ToolStripMenuItem Header(string text)
    {
        return new ToolStripMenuItem(text) { Enabled = false };
    }

    void ShowAppMenu(Control anchor)
    {
        if (menu != null) menu.Dispose();
        menu = new ContextMenuStrip { Renderer = renderer, Font = Theme.Body, ShowCheckMargin = true, ShowImageMargin = false };

        menu.Items.Add(Header("Preset"));
        foreach (var p in presets.ToList())
        {
            var preset = p;
            menu.Items.Add(Item(PresetLabel(preset), () => RunPreset(preset)));
        }
        if (presets.Count == 0) menu.Items.Add(Header("   Belum ada preset"));
        menu.Items.Add(Item("Simpan tata letak sekarang sebagai preset…", SavePreset));
        if (presets.Count > 0)
        {
            var del = new ToolStripMenuItem("Hapus preset") { Padding = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            del.DropDown.Renderer = renderer;
            del.DropDown.Font = Theme.Body;
            foreach (var p in presets.ToList())
            {
                var preset = p;
                del.DropDownItems.Add(Item(preset.Name, () => DeletePreset(preset)));
            }
            menu.Items.Add(del);
        }

        menu.Items.Add(new ToolStripSeparator());
        var closeAll = Item("Tutup semua instance", CloseAll);
        closeAll.Tag = "danger";
        menu.Items.Add(closeAll);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Check("Jalan otomatis saat Windows nyala", settings.AutoStart, on =>
        {
            try
            {
                AppSettings.ApplyAutoStart(on);
                settings.AutoStart = on;
                settings.Save();
            }
            catch (Exception ex) { DarkDialog.Alert(this, "Gagal mengubah", ex.Message); }
        }));
        menu.Items.Add(Check("Buka & rapikan otomatis saat launcher jalan otomatis", settings.ArrangeOnStart, on =>
        {
            settings.ArrangeOnStart = on;
            settings.Save();
        }));
        menu.Items.Add(Check("Tombol × menyembunyikan ke tray", settings.CloseToTray, on =>
        {
            settings.CloseToTray = on;
            settings.Save();
        }));
        menu.Items.Add(Check("Hotkey global (Ctrl+Alt+1–9, Ctrl+Alt+0)", settings.Hotkeys, on =>
        {
            settings.Hotkeys = on;
            settings.Save();
            if (on) RegisterHotkeys(); else UnregisterHotkeys();
        }));
        menu.Items.Add(Item("Daftar hotkey…", ShowHotkeyHelp));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Keluar dari launcher", ExitApp));

        menu.Show(anchor, new Point(0, anchor.Height + Theme.S(4)));
    }

    void ShowHotkeyHelp()
    {
        var lines = new List<string> { "Ctrl+Alt+0  →  Buka & rapikan" };
        for (int i = 0; i < Math.Min(9, profiles.Count); i++) lines.Add("Ctrl+Alt+" + (i + 1) + "  →  " + profiles[i].Name);
        lines.Add("");
        lines.Add(settings.Hotkeys ? "Hotkey sedang aktif. Nomor mengikuti urutan daftar." : "Hotkey sedang nonaktif. Aktifkan dari menu ≡.");
        if (settings.Hotkeys && !hotkeysRegistered) lines.Add("Sebagian hotkey mungkin dipakai aplikasi lain.");
        DarkDialog.Alert(this, "Hotkey global", string.Join("\n", lines));
    }

    void FillTrayMenu(ContextMenuStrip m)
    {
        m.Items.Clear();
        m.Items.Add(Item("Buka Claude Launcher", ShowFromTray));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Header("Instance"));
        for (int i = 0; i < profiles.Count; i++)
        {
            var p = profiles[i];
            var on = running.Contains(Claude.Key(p));
            var label = (on ? "●  " : "○  ") + p.Name + (i < 9 ? "\tCtrl+Alt+" + (i + 1) : "");
            m.Items.Add(Item(label, () => Launch(p)));
        }
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Item("Buka & rapikan (" + profiles.Count(p => layout.Includes(p)) + ")", OpenAndArrange));
        if (presets.Count > 0)
        {
            var sub = new ToolStripMenuItem("Preset") { Padding = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            sub.DropDown.Renderer = renderer;
            sub.DropDown.Font = Theme.Body;
            foreach (var p in presets.ToList())
            {
                var preset = p;
                sub.DropDownItems.Add(Item(PresetLabel(preset), () => RunPreset(preset)));
            }
            m.Items.Add(sub);
        }
        var closeAll = Item("Tutup semua instance", CloseAll);
        closeAll.Tag = "danger";
        m.Items.Add(closeAll);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(Item("Keluar", ExitApp));
    }
}
