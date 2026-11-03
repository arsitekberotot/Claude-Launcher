using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main()
    {
        bool created;
        using (var mutex = new Mutex(true, "ClaudeLauncher.SingleInstance", out created))
        {
            if (!created)
            {
                HotkeyNative.PostMessage(HotkeyNative.HWND_BROADCAST, HotkeyNative.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                return;
            }
            try { Native.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool autostart = Environment.GetCommandLineArgs().Contains("--autostart");
            var form = new LauncherForm(autostart);
            if (autostart && form.StartHidden)
            {
                var handle = form.Handle; // create the window (hotkeys, tray) without showing it
            }
            else
            {
                form.Show();
            }
            Application.Run();
        }
    }
}

// ---------------------------------------------------------------- native

static class Native
{
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hWnd, string app, string idList);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr hMonitor, int type, out uint dpiX, out uint dpiY);

    public static float MonitorScale(IntPtr hMonitor)
    {
        try
        {
            uint x, y;
            if (GetDpiForMonitor(hMonitor, 0, out x, out y) == 0) return x / 96f; // MDT_EFFECTIVE_DPI
        }
        catch { }
        return 1f;
    }

    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT rect, int size);

    public static void Restore(IntPtr hWnd)
    {
        if (IsZoomed(hWnd) || IsIconic(hWnd)) ShowWindow(hWnd, 9); // SW_RESTORE
    }

    // Physical-pixel coordinates for this thread while arranging windows across monitors.
    public static IntPtr EnterPerMonitorDpi()
    {
        try { return SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch { return IntPtr.Zero; }
    }

    public static void LeaveDpi(IntPtr previous)
    {
        if (previous == IntPtr.Zero) return;
        try { SetThreadDpiAwarenessContext(previous); } catch { }
    }

    public static void DarkTitleBar(IntPtr hWnd, Color caption)
    {
        try
        {
            int on = 1;
            DwmSetWindowAttribute(hWnd, 20, ref on, 4);      // DWMWA_USE_IMMERSIVE_DARK_MODE
            int color = caption.R | (caption.G << 8) | (caption.B << 16);
            DwmSetWindowAttribute(hWnd, 35, ref color, 4);   // DWMWA_CAPTION_COLOR (Windows 11)
        }
        catch { }
    }

    public static void DarkScrollbars(IntPtr hWnd)
    {
        try { SetWindowTheme(hWnd, "DarkMode_Explorer", null); } catch { }
    }

    public static void FocusOtherInstance()
    {
        var me = Process.GetCurrentProcess();
        foreach (var p in Process.GetProcessesByName(me.ProcessName))
        {
            if (p.Id == me.Id || p.MainWindowHandle == IntPtr.Zero) continue;
            if (IsIconic(p.MainWindowHandle)) ShowWindow(p.MainWindowHandle, 9);
            SetForegroundWindow(p.MainWindowHandle);
            return;
        }
    }
}

// ---------------------------------------------------------------- theme

static class Theme
{
    // Monochrome, pure-black palette; white is the only accent.
    public static readonly Color Bg = Color.FromArgb(0, 0, 0);
    public static readonly Color Surface = Color.FromArgb(17, 17, 17);
    public static readonly Color SurfaceHover = Color.FromArgb(26, 26, 26);
    public static readonly Color Border = Color.FromArgb(38, 38, 38);
    public static readonly Color BorderHover = Color.FromArgb(58, 58, 58);
    public static readonly Color Text = Color.FromArgb(250, 250, 250);
    public static readonly Color Muted = Color.FromArgb(140, 140, 140);
    public static readonly Color Accent = Color.FromArgb(250, 250, 250);
    public static readonly Color AccentHover = Color.FromArgb(226, 226, 226);
    public static readonly Color OnAccent = Color.FromArgb(0, 0, 0);
    public static readonly Color Green = Color.FromArgb(74, 222, 128);
    public static readonly Color Danger = Color.FromArgb(248, 113, 113);
    public static readonly Color DangerHover = Color.FromArgb(252, 140, 140);

    public static readonly Color[] Palette =
    {
        Color.FromArgb(217, 119, 87),
        Color.FromArgb(91, 155, 213),
        Color.FromArgb(98, 180, 128),
        Color.FromArgb(170, 132, 214),
        Color.FromArgb(214, 168, 72),
        Color.FromArgb(214, 112, 150),
        Color.FromArgb(72, 172, 166),
        Color.FromArgb(128, 134, 142)
    };
    public static readonly string[] PaletteNames =
    {
        "Oranye", "Biru", "Hijau", "Ungu", "Kuning", "Merah muda", "Toska", "Abu-abu"
    };

    public static readonly Font Body = new Font("Segoe UI", 9.75f);
    public static readonly Font Small = new Font("Segoe UI", 9f);
    public static readonly Font Input = new Font("Segoe UI", 10.5f);
    public static readonly Font Name = new Font("Segoe UI Semibold", 10.5f);
    public static readonly Font Button = new Font("Segoe UI Semibold", 9.5f);
    public static readonly Font Title = new Font("Segoe UI Semibold", 18f);
    public static readonly Font Avatar = new Font("Segoe UI Semibold", 12f);

    static float scale;

    public static int S(float px)
    {
        if (scale == 0)
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
        }
        return (int)Math.Round(px * scale);
    }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static string ToHex(Color c)
    {
        return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
    }

    public static Color FromHex(string hex, Color fallback)
    {
        try { return ColorTranslator.FromHtml(hex.Trim()); } catch { return fallback; }
    }

    public static Bitmap Swatch(Color c)
    {
        int size = S(14);
        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        using (var b = new SolidBrush(c))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillEllipse(b, 0, 0, size - 1, size - 1);
        }
        return bmp;
    }
}

// ---------------------------------------------------------------- data

class Profile
{
    public string Id;
    public string Name;
    public Color Color;
    public bool IsTemplate;

    public bool IsDefault { get { return Id == Store.DefaultId; } }

    // Folder passed as --user-data-dir; null for the app's own profile.
    public string DataDir
    {
        get { return IsDefault ? null : Path.Combine(Store.Home, ".claude-" + Id); }
    }

    // Folder that actually holds this profile's files.
    public string StorageDir
    {
        get { return IsDefault ? Store.DefaultStorageDir() : DataDir; }
    }

    public string Subtitle
    {
        get { return IsDefault ? "Profil bawaan aplikasi" : "~\\.claude-" + Id; }
    }
}

static class Store
{
    public const string DefaultId = "default";
    public static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static readonly string Root = Path.Combine(Home, ".claude-launcher");
    static readonly string ConfigPath = Path.Combine(Root, "profiles.txt");

    // The Store build of Claude keeps its default profile outside %APPDATA%.
    public static string DefaultStorageDir()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Claude-Data");
        if (Directory.Exists(root))
        {
            var dir = Directory.GetDirectories(root, "claude_*").FirstOrDefault();
            if (dir != null) return dir;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");
    }

    public static Profile Template(List<Profile> list)
    {
        return list.FirstOrDefault(p => p.IsTemplate);
    }

    // profiles.txt: one profile per line, "id|display name|#RRGGBB|t" (t = template).
    public static List<Profile> Load()
    {
        var list = new List<Profile>();
        if (File.Exists(ConfigPath))
        {
            foreach (var raw in File.ReadAllLines(ConfigPath))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var parts = line.Split('|');
                var id = parts[0].Trim();
                if (id.Length == 0 || list.Any(p => Same(p.Id, id))) continue;
                var fallback = Theme.Palette[list.Count % Theme.Palette.Length];
                list.Add(new Profile
                {
                    Id = id,
                    Name = parts.Length > 1 && parts[1].Trim().Length > 0 ? parts[1].Trim() : DefaultName(id),
                    Color = parts.Length > 2 ? Theme.FromHex(parts[2], fallback) : fallback,
                    IsTemplate = parts.Length > 3 && parts[3].Trim() == "t" && Template(list) == null
                });
            }
            return list;
        }

        foreach (var id in new[] { DefaultId, "second", "third", "fourth", "fifth" })
        {
            list.Add(new Profile
            {
                Id = id,
                Name = DefaultName(id),
                Color = Theme.Palette[list.Count % Theme.Palette.Length]
            });
        }
        Save(list);
        return list;
    }

    public static void Save(List<Profile> list)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllLines(ConfigPath,
            list.Select(p => p.Id + "|" + p.Name.Replace("|", "/") + "|" + Theme.ToHex(p.Color) + (p.IsTemplate ? "|t" : "")));
    }

    public static string NewId(string name, List<Profile> existing)
    {
        var slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length == 0) slug = "instance";
        var id = slug;
        int n = 2;
        while (Same(id, DefaultId) || existing.Any(p => Same(p.Id, id)))
        {
            id = slug + "-" + n;
            n++;
        }
        return id;
    }

    static string DefaultName(string id)
    {
        if (id == DefaultId) return "Utama";
        return char.ToUpperInvariant(id[0]) + id.Substring(1);
    }

    static bool Same(string a, string b)
    {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}

// ---------------------------------------------------------------- claude process

static class Claude
{
    static volatile string exePath;

    public static string Key(Profile p)
    {
        return p.IsDefault ? Store.DefaultId : Normalize(p.DataDir);
    }

    static string Normalize(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd('\\'); } catch { return path; }
    }

    public static HashSet<string> RunningKeys()
    {
        return new HashSet<string>(RunningProcesses().Keys, StringComparer.OrdinalIgnoreCase);
    }

    // Profile key -> PID of its main Claude desktop process.
    public static Dictionary<string, int> RunningProcesses()
    {
        var set = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using (var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine, ExecutablePath FROM Win32_Process WHERE Name='Claude.exe'"))
            {
                foreach (ManagementObject p in searcher.Get())
                {
                    var cmd = p["CommandLine"] as string;
                    var exe = p["ExecutablePath"] as string;
                    if (cmd == null) continue;
                    if (exe != null && !exe.EndsWith(@"\app\Claude.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (cmd.Contains("--type=")) continue; // Electron child process

                    if (exe != null && exePath == null) exePath = exe;

                    var pid = Convert.ToInt32(p["ProcessId"]);
                    var m = Regex.Match(cmd, "--user-data-dir=(?:\"([^\"]+)\"|(\\S+))");
                    if (m.Success)
                        set[Normalize(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)] = pid;
                    else
                        set[Store.DefaultId] = pid;
                }
            }
        }
        catch { }
        return set;
    }

    static string Exe()
    {
        var cached = exePath;
        if (cached != null && File.Exists(cached)) return cached;
        exePath = null;
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -Command \"Get-AppxPackage *claude* | Where-Object { Test-Path (Join-Path $_.InstallLocation 'app\\Claude.exe') } | Select-Object -First 1 -ExpandProperty InstallLocation\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                var output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                if (output.Length > 0)
                {
                    var candidate = Path.Combine(output, "app", "Claude.exe");
                    if (File.Exists(candidate)) exePath = candidate;
                }
            }
        }
        catch { }
        return exePath;
    }

    public static void Launch(Profile p)
    {
        var exe = Exe();
        if (exe == null) throw new InvalidOperationException("Claude.exe tidak ditemukan. Pastikan Claude Desktop terinstall.");
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        if (!p.IsDefault) psi.Arguments = "--user-data-dir=\"" + p.DataDir + "\"";
        Process.Start(psi);
    }
}

// ---------------------------------------------------------------- signed-in account

// Reads the signed-in email from Claude's cached claude.ai account data. Only the
// account uuid (config.json) and the cached account record are read; never tokens.
static class AccountInfo
{
    static readonly Dictionary<string, KeyValuePair<DateTime, string>> cache =
        new Dictionary<string, KeyValuePair<DateTime, string>>(StringComparer.OrdinalIgnoreCase);
    static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    public static string Email(string dataDir)
    {
        try
        {
            var configPath = Path.Combine(dataDir, "config.json");
            var blobDir = Path.Combine(dataDir, "IndexedDB", "https_claude.ai_0.indexeddb.blob");
            if (!File.Exists(configPath) || !Directory.Exists(blobDir)) return null;

            var files = new DirectoryInfo(blobDir).GetFiles("*", SearchOption.AllDirectories)
                .Where(f => f.Length < 50 * 1024 * 1024)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            var stamp = new[] { File.GetLastWriteTimeUtc(configPath) }
                .Concat(files.Select(f => f.LastWriteTimeUtc)).Max();
            KeyValuePair<DateTime, string> hit;
            if (cache.TryGetValue(dataDir, out hit) && hit.Key == stamp) return hit.Value;

            string email = null;
            var m = Regex.Match(ReadShared(configPath, Encoding.UTF8), "\"lastKnownAccountUuid\"\\s*:\\s*\"([0-9a-fA-F-]{36})\"");
            if (m.Success)
            {
                foreach (var f in files)
                {
                    email = FindEmail(ReadShared(f.FullName, Latin1), m.Groups[1].Value);
                    if (email != null) break;
                }
            }
            cache[dataDir] = new KeyValuePair<DateTime, string>(stamp, email);
            return email;
        }
        catch { return null; }
    }

    static string ReadShared(string path, Encoding encoding)
    {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(fs, encoding))
            return reader.ReadToEnd();
    }

    // The cache is V8-serialized: "email_address" is followed by '"', a varint length, then the bytes.
    static string FindEmail(string s, string uuid)
    {
        const string key = "email_address";
        for (int i = s.IndexOf(uuid, StringComparison.Ordinal); i >= 0; i = s.IndexOf(uuid, i + 1, StringComparison.Ordinal))
        {
            int k = s.IndexOf(key, i, Math.Min(400, s.Length - i), StringComparison.Ordinal);
            if (k < 0) continue;
            int p = k + key.Length;
            if (p >= s.Length || s[p] != '"') continue;
            p++;
            int length = 0, shift = 0;
            while (p < s.Length && shift < 28)
            {
                int b = s[p++];
                length |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            if (length <= 0 || length > 254 || p + length > s.Length) continue;
            var value = s.Substring(p, length);
            if (Regex.IsMatch(value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) return value;
        }
        return null;
    }
}

// ---------------------------------------------------------------- window layout

class MonitorInfo
{
    public string Device;
    public Rectangle Bounds, Work;
    public bool Primary;
    public int Number;
    public float Scale = 1f;
}

class ZoneLayout
{
    public string Name;
    public List<RectangleF> Zones = new List<RectangleF>();

    public ZoneLayout Clone()
    {
        return new ZoneLayout { Name = Name, Zones = new List<RectangleF>(Zones) };
    }
}

// Zones are stored relative to the monitor work area (0..1).
static class Layouts
{
    public const string CustomPrefix = "custom:";
    public static readonly string[] TemplateKeys = { "focus", "columns", "rows", "grid", "priority" };
    public static readonly string[] TemplateNames = { "Focus", "Kolom", "Baris", "Grid", "Priority Grid" };
    static readonly string FilePath = Path.Combine(Store.Root, "layouts.txt");
    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    public static List<RectangleF> Template(string key, int n)
    {
        n = Math.Max(1, n);
        var z = new List<RectangleF>();
        if (key == "columns")
        {
            for (int i = 0; i < n; i++) z.Add(new RectangleF(i / (float)n, 0, 1f / n, 1));
        }
        else if (key == "rows")
        {
            for (int i = 0; i < n; i++) z.Add(new RectangleF(0, i / (float)n, 1, 1f / n));
        }
        else if (key == "focus")
        {
            float step = n == 1 ? 0 : Math.Min(0.05f, 0.3f / (n - 1));
            for (int i = 0; i < n; i++) z.Add(new RectangleF(0.15f + i * step, 0.1f + i * step, 0.6f, 0.65f));
        }
        else if (key == "priority" && n >= 3)
        {
            z.Add(new RectangleF(0.25f, 0, 0.5f, 1));
            int rest = n - 1, left = (rest + 1) / 2, right = rest - left;
            for (int i = 0; i < left; i++) z.Add(new RectangleF(0, i / (float)left, 0.25f, 1f / left));
            for (int i = 0; i < right; i++) z.Add(new RectangleF(0.75f, i / (float)right, 0.25f, 1f / right));
        }
        else if (key == "priority")
        {
            return Template("columns", n);
        }
        else // grid: up to 3 side by side, then rows of a square-ish grid; last row stretches
        {
            int cols = n <= 3 ? n : (int)Math.Ceiling(Math.Sqrt(n));
            int rows = (int)Math.Ceiling(n / (double)cols);
            for (int i = 0; i < n; i++)
            {
                int row = i / cols, col = i % cols;
                int inRow = row == rows - 1 ? n - row * cols : cols;
                z.Add(new RectangleF(col / (float)inRow, row / (float)rows, 1f / inRow, 1f / rows));
            }
        }
        return z;
    }

    public static List<ZoneLayout> LoadCustom()
    {
        var list = new List<ZoneLayout>();
        if (!File.Exists(FilePath)) return list;
        foreach (var line in File.ReadAllLines(FilePath))
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            var layout = new ZoneLayout { Name = line.Substring(0, tab) };
            foreach (var part in line.Substring(tab + 1).Split(';'))
            {
                var v = part.Split(',');
                float x, y, w, h;
                if (v.Length == 4 &&
                    float.TryParse(v[0], System.Globalization.NumberStyles.Float, Inv, out x) &&
                    float.TryParse(v[1], System.Globalization.NumberStyles.Float, Inv, out y) &&
                    float.TryParse(v[2], System.Globalization.NumberStyles.Float, Inv, out w) &&
                    float.TryParse(v[3], System.Globalization.NumberStyles.Float, Inv, out h))
                {
                    layout.Zones.Add(new RectangleF(x, y, w, h));
                }
            }
            if (layout.Zones.Count > 0 && !list.Any(l => l.Name == layout.Name)) list.Add(layout);
        }
        return list;
    }

    public static void SaveCustom(List<ZoneLayout> list)
    {
        Directory.CreateDirectory(Store.Root);
        File.WriteAllLines(FilePath, list.Select(l => l.Name.Replace("\t", " ") + "\t" + string.Join(";",
            l.Zones.Select(z => string.Join(",", new[] { z.X, z.Y, z.Width, z.Height }.Select(f => f.ToString("0.####", Inv)))))));
    }

    // Zones for a settings key ("grid", "custom:Name", ...) and number of windows.
    public static List<RectangleF> Resolve(string key, int n, List<ZoneLayout> customs)
    {
        if (key.StartsWith(CustomPrefix))
        {
            var name = key.Substring(CustomPrefix.Length);
            var custom = customs.FirstOrDefault(c => c.Name == name);
            if (custom != null) return custom.Zones;
            key = "grid";
        }
        return Template(TemplateKeys.Contains(key) ? key : "grid", n);
    }

    // Draws zones scaled into rect, numbered.
    public static void DrawPreview(Graphics g, Rectangle rect, List<RectangleF> zones, Color areaColor)
    {
        using (var b = new SolidBrush(areaColor)) g.FillRectangle(b, rect);
        int gap = Theme.S(2);
        using (var zoneBrush = new SolidBrush(Theme.Bg))
        using (var pen = new Pen(Theme.Border))
        {
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                int x1 = rect.X + (int)Math.Round(z.X * rect.Width), x2 = rect.X + (int)Math.Round(z.Right * rect.Width);
                int y1 = rect.Y + (int)Math.Round(z.Y * rect.Height), y2 = rect.Y + (int)Math.Round(z.Bottom * rect.Height);
                var r = Rectangle.FromLTRB(x1 + gap / 2, y1 + gap / 2, x2 - gap / 2, y2 - gap / 2);
                if (r.Width < 2 || r.Height < 2) continue;
                g.FillRectangle(zoneBrush, r);
                g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
                if (r.Width > Theme.S(18) && r.Height > Theme.S(18))
                {
                    TextRenderer.DrawText(g, (i + 1).ToString(), Theme.Small, r, Theme.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }
    }
}

class LayoutSettings
{
    public string Monitor = "";
    public string Layout = "grid";
    public bool Gap = true;
    public List<string> Ids; // null = all profiles

    static readonly string FilePath = Path.Combine(Store.Root, "layout.txt");

    public bool Includes(Profile p)
    {
        return Ids == null || Ids.Contains(p.Id, StringComparer.OrdinalIgnoreCase);
    }

    public static LayoutSettings Load()
    {
        var s = new LayoutSettings();
        if (!File.Exists(FilePath)) return s;
        foreach (var line in File.ReadAllLines(FilePath))
        {
            var i = line.IndexOf('=');
            if (i <= 0) continue;
            var key = line.Substring(0, i).Trim();
            var value = line.Substring(i + 1).Trim();
            if (key == "monitor") s.Monitor = value;
            else if (key == "layout") s.Layout = value == "auto" ? "grid" : value;
            else if (key == "gap") s.Gap = value == "1";
            else if (key == "ids") s.Ids = value == "*" ? null : value.Split(',').Where(x => x.Length > 0).ToList();
        }
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(Store.Root);
        File.WriteAllLines(FilePath, new[]
        {
            "monitor=" + Monitor,
            "layout=" + Layout,
            "gap=" + (Gap ? "1" : "0"),
            "ids=" + (Ids == null ? "*" : string.Join(",", Ids))
        });
    }
}

static class WindowArranger
{
    const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    public static List<MonitorInfo> Monitors()
    {
        var list = new List<MonitorInfo>();
        var previous = Native.EnterPerMonitorDpi();
        try
        {
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref Native.RECT r, IntPtr data) =>
            {
                var info = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX)) };
                if (Native.GetMonitorInfo(h, ref info))
                {
                    list.Add(new MonitorInfo
                    {
                        Device = info.szDevice,
                        Bounds = ToRect(info.rcMonitor),
                        Work = ToRect(info.rcWork),
                        Primary = (info.dwFlags & 1) != 0,
                        Scale = Native.MonitorScale(h)
                    });
                }
                return true;
            }, IntPtr.Zero);
        }
        finally { Native.LeaveDpi(previous); }
        // Primary is 1, the rest left to right.
        list = list.OrderByDescending(m => m.Primary).ThenBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ToList();
        for (int i = 0; i < list.Count; i++) list[i].Number = i + 1;
        return list;
    }

    public static MonitorInfo Find(string device)
    {
        var all = Monitors();
        return all.FirstOrDefault(m => m.Device == device)
            ?? all.FirstOrDefault(m => m.Primary)
            ?? all.FirstOrDefault();
    }

    // Largest visible, titled, top-level window owned by the process.
    public static IntPtr MainWindow(int pid)
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;
        Native.EnumWindows((h, l) =>
        {
            int owner;
            Native.GetWindowThreadProcessId(h, out owner);
            if (owner != pid || !Native.IsWindowVisible(h)) return true;
            if (Native.GetWindow(h, 4) != IntPtr.Zero || Native.GetWindowTextLength(h) == 0) return true; // GW_OWNER
            Native.RECT r;
            Native.GetWindowRect(h, out r);
            long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
            if (area > bestArea) { bestArea = area; best = h; }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    // Window i goes to zone i; extra windows wrap around to the first zones.
    public static void Arrange(List<IntPtr> windows, MonitorInfo monitor, List<RectangleF> zones, bool gap)
    {
        if (windows.Count == 0 || monitor == null || zones.Count == 0) return;
        var previous = Native.EnterPerMonitorDpi();
        try
        {
            int g = gap ? (int)Math.Round(8 * monitor.Scale) : 0;
            var area = monitor.Work;
            var inner = new Rectangle(area.X + g / 2, area.Y + g / 2, area.Width - g, area.Height - g);
            for (int i = 0; i < windows.Count; i++)
            {
                var z = zones[i % zones.Count];
                int x1 = inner.X + (int)Math.Round(z.X * inner.Width), x2 = inner.X + (int)Math.Round(z.Right * inner.Width);
                int y1 = inner.Y + (int)Math.Round(z.Y * inner.Height), y2 = inner.Y + (int)Math.Round(z.Bottom * inner.Height);
                var cell = Rectangle.FromLTRB(x1 + g / 2, y1 + g / 2, x2 - g / 2, y2 - g / 2);
                Native.Restore(windows[i]);
                Place(windows[i], cell);
            }
        }
        finally { Native.LeaveDpi(previous); }
    }

    // Moves the window so its visible frame (without the invisible resize border) fills the cell.
    static void Place(IntPtr h, Rectangle cell)
    {
        // Twice: the first move may hop monitors and trigger a DPI resize.
        for (int pass = 0; pass < 2; pass++)
        {
            Native.RECT win, frame;
            Native.GetWindowRect(h, out win);
            int l = 0, t = 0, r = 0, b = 0;
            if (Native.DwmGetWindowAttribute(h, 9, out frame, Marshal.SizeOf(typeof(Native.RECT))) == 0) // DWMWA_EXTENDED_FRAME_BOUNDS
            {
                l = frame.Left - win.Left;
                t = frame.Top - win.Top;
                r = win.Right - frame.Right;
                b = win.Bottom - frame.Bottom;
            }
            Native.SetWindowPos(h, IntPtr.Zero, cell.X - l, cell.Y - t, cell.Width + l + r, cell.Height + t + b,
                SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    static Rectangle ToRect(Native.RECT r)
    {
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }
}

// ---------------------------------------------------------------- setup copy

class CopyOptions
{
    public bool Mcp = true, Prefs = true, ToolToggles = true, Presets = true, Extensions = true;
}

// Copies app setup between profiles. Login (config.json, cookies) is never copied.
static class SetupCopier
{
    const string DesktopConfig = "claude_desktop_config.json";
    static readonly string[] ExtensionDirs = { "Claude Extensions", "Claude Extensions Settings" };
    // Per-machine session/sidebar state that should not follow a profile.
    static readonly string[] SkipPrefKeys = { "epitaxyPrefs", "launchPreviewPersistedWorkspaces", "launchPreviewSessionScopedSessions" };

    public static string Copy(Profile from, Profile to, CopyOptions o)
    {
        var src = from.StorageDir;
        var dst = to.StorageDir;
        if (!Directory.Exists(src)) throw new InvalidOperationException("Folder data \"" + from.Name + "\" belum ada. Buka instance itu sekali dulu.");
        Directory.CreateDirectory(dst);
        var backup = Path.Combine(dst, "launcher-backup", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var done = new List<string>();

        if (o.Mcp || o.Prefs)
        {
            var srcCfg = ReadJson(Path.Combine(src, DesktopConfig));
            var dstPath = Path.Combine(dst, DesktopConfig);
            var dstCfg = ReadJson(dstPath);

            if (o.Mcp)
            {
                object servers;
                if (srcCfg.TryGetValue("mcpServers", out servers)) dstCfg["mcpServers"] = servers;
                else dstCfg.Remove("mcpServers");
                var dict = servers as Dictionary<string, object>;
                done.Add("MCP server (" + (dict == null ? 0 : dict.Count) + ")");
            }

            if (o.Prefs)
            {
                foreach (var kv in srcCfg)
                {
                    if (kv.Key == "mcpServers" || kv.Key == "preferences") continue;
                    dstCfg[kv.Key] = kv.Value;
                }
                object srcPrefsObj, dstPrefsObj;
                srcCfg.TryGetValue("preferences", out srcPrefsObj);
                dstCfg.TryGetValue("preferences", out dstPrefsObj);
                var srcPrefs = srcPrefsObj as Dictionary<string, object>;
                var dstPrefs = dstPrefsObj as Dictionary<string, object> ?? new Dictionary<string, object>();
                if (srcPrefs != null)
                {
                    foreach (var kv in srcPrefs)
                    {
                        if (!SkipPrefKeys.Contains(kv.Key)) dstPrefs[kv.Key] = kv.Value;
                    }
                }
                dstCfg["preferences"] = dstPrefs;
                done.Add("Preferensi aplikasi");
            }

            BackupFile(dstPath, backup);
            File.WriteAllText(dstPath, Json().Serialize(dstCfg));
        }

        if (o.ToolToggles && CopyFile(src, dst, "mcp-user-tool-toggles.json", backup)) done.Add("Status tool MCP");
        if (o.Presets && CopyFile(src, dst, "ccd-session-presets.json", backup)) done.Add("Preset sesi Claude Code");

        if (o.Extensions)
        {
            int copied = 0;
            foreach (var name in ExtensionDirs)
            {
                var s = Path.Combine(src, name);
                if (!Directory.Exists(s)) continue;
                var d = Path.Combine(dst, name);
                if (Directory.Exists(d)) CopyDir(d, Path.Combine(backup, name));
                CopyDir(s, d);
                copied++;
            }
            if (copied > 0) done.Add("Extension");
        }

        if (done.Count == 0) return "Tidak ada yang disalin. Sumber belum punya setup yang dipilih.";
        var summary = "Disalin: " + string.Join(", ", done) + ".";
        if (Directory.Exists(backup)) summary += "\n\nFile lama dibackup ke:\n" + backup;
        return summary;
    }

    static System.Web.Script.Serialization.JavaScriptSerializer Json()
    {
        return new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    }

    static Dictionary<string, object> ReadJson(string path)
    {
        if (!File.Exists(path)) return new Dictionary<string, object>();
        var obj = Json().DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
        return obj ?? new Dictionary<string, object>();
    }

    static void BackupFile(string path, string backupDir)
    {
        if (!File.Exists(path)) return;
        Directory.CreateDirectory(backupDir);
        File.Copy(path, Path.Combine(backupDir, Path.GetFileName(path)), true);
    }

    static bool CopyFile(string src, string dst, string name, string backupDir)
    {
        var s = Path.Combine(src, name);
        if (!File.Exists(s)) return false;
        var d = Path.Combine(dst, name);
        BackupFile(d, backupDir);
        File.Copy(s, d, true);
        return true;
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}

// ---------------------------------------------------------------- controls

class DarkCheck : Control
{
    bool isChecked = true;
    public bool Checked
    {
        get { return isChecked; }
        set { isChecked = value; Invalidate(); }
    }

    public DarkCheck(string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text;
        Font = Theme.Body;
        ForeColor = Theme.Text;
        BackColor = Theme.Bg;
        Cursor = Cursors.Hand;
        Height = Theme.S(28);
        Click += (s, e) => Checked = !Checked;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int box = Theme.S(18);
        var r = new RectangleF(0.5f, (Height - box) / 2f, box, box);
        using (var path = Theme.Round(r, Theme.S(5)))
        {
            if (isChecked)
            {
                using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
                using (var pen = new Pen(Theme.OnAccent, Theme.S(2)))
                {
                    g.DrawLines(pen, new[]
                    {
                        new PointF(r.X + box * 0.25f, r.Y + box * 0.52f),
                        new PointF(r.X + box * 0.43f, r.Y + box * 0.70f),
                        new PointF(r.X + box * 0.76f, r.Y + box * 0.32f)
                    });
                }
            }
            else
            {
                using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (var pen = new Pen(Theme.BorderHover)) g.DrawPath(pen, path);
            }
        }
        int tx = box + Theme.S(10);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(tx, 0, Width - tx, Height), ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

class FlatButton : Control
{
    public Color BaseColor = Theme.Surface;
    public Color HoverColor = Theme.SurfaceHover;
    public Color BorderColor = Color.Empty;
    public int Radius = 9999; // clamped to half the height: capsule
    bool hover, down;

    public FlatButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Cursor = Cursors.Hand;
        Font = Theme.Button;
        ForeColor = Theme.Text;
    }

    public static FlatButton Primary(string text)
    {
        return new FlatButton { Text = text, BaseColor = Theme.Accent, HoverColor = Theme.AccentHover, ForeColor = Theme.OnAccent };
    }

    public static FlatButton Secondary(string text)
    {
        return new FlatButton { Text = text, BorderColor = Theme.Border };
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var fill = hover ? HoverColor : BaseColor;
        if (down) fill = ControlPaint.Dark(fill, 0.05f);
        using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius))
        using (var b = new SolidBrush(fill))
        {
            g.FillPath(b, path);
            if (BorderColor != Color.Empty)
            {
                using (var pen = new Pen(hover ? Theme.BorderHover : BorderColor)) g.DrawPath(pen, path);
            }
        }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor, fill,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

class ToggleSwitch : Control
{
    bool on;
    public event EventHandler Toggled;

    public bool On
    {
        get { return on; }
        set { if (on == value) return; on = value; Invalidate(); }
    }

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(Theme.S(40), Theme.S(22));
        Cursor = Cursors.Hand;
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        On = !On;
        if (Toggled != null) Toggled(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.Round(track, track.Height / 2))
        {
            using (var b = new SolidBrush(on ? Theme.Accent : Theme.Bg)) g.FillPath(b, path);
            using (var pen = new Pen(on ? Theme.Accent : Theme.BorderHover)) g.DrawPath(pen, path);
        }
        float pad = Theme.S(3), d = Height - 2 * pad - 1;
        float x = on ? Width - pad - d - 1 : pad;
        using (var b = new SolidBrush(on ? Theme.OnAccent : Theme.Muted)) g.FillEllipse(b, x, pad, d, d);
    }
}

class ProfileCard : Control
{
    public readonly Profile Profile;
    public bool Running { get; private set; }
    readonly FlatButton openBtn, moreBtn;
    readonly ToggleSwitch activeSwitch;
    bool hover;

    public ProfileCard(Profile profile, bool active, Action<Profile> launch, Action<ProfileCard, Control> showMenu,
                       Action<Profile, bool> setActive)
    {
        Profile = profile;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = Theme.S(78);
        Margin = new Padding(0, 0, 0, Theme.S(10));
        BackColor = Theme.Bg;
        Cursor = Cursors.Default;

        openBtn = FlatButton.Primary("Buka");
        openBtn.Size = new Size(Theme.S(100), Theme.S(36));
        moreBtn = FlatButton.Secondary("⋯");
        moreBtn.Font = Theme.Name;
        moreBtn.Size = new Size(Theme.S(36), Theme.S(36));
        openBtn.Click += (s, e) => launch(Profile);
        moreBtn.Click += (s, e) => showMenu(this, moreBtn);
        activeSwitch = new ToggleSwitch { On = active };
        activeSwitch.Toggled += (s, e) => setActive(Profile, activeSwitch.On);
        new ToolTip().SetToolTip(activeSwitch, "Ikut \"Buka & rapikan\"");
        foreach (var c in new Control[] { openBtn, moreBtn, activeSwitch })
        {
            c.MouseEnter += (s, e) => SetHover(true);
            c.MouseLeave += (s, e) => SetHover(ClientRectangle.Contains(PointToClient(Cursor.Position)));
        }
        Controls.Add(openBtn);
        Controls.Add(moreBtn);
        Controls.Add(activeSwitch);
        SetHover(false);

        DoubleClick += (s, e) => launch(Profile);
        MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) showMenu(this, null); };
    }

    public string Email { get; private set; }

    public void SetRunning(bool running)
    {
        if (Running == running) return;
        Running = running;
        openBtn.Text = running ? "Tampilkan" : "Buka";
        Invalidate();
    }

    public void SetEmail(string email)
    {
        if (Email == email) return;
        Email = email;
        Invalidate();
    }

    void SetHover(bool value)
    {
        hover = value;
        var fill = hover ? Theme.SurfaceHover : Theme.Surface;
        openBtn.BackColor = fill;
        moreBtn.BackColor = fill;
        moreBtn.BaseColor = fill;
        if (activeSwitch != null) activeSwitch.BackColor = fill;
        openBtn.Invalidate();
        moreBtn.Invalidate();
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { SetHover(true); base.OnMouseEnter(e); }

    protected override void OnMouseLeave(EventArgs e)
    {
        SetHover(ClientRectangle.Contains(PointToClient(Cursor.Position)));
        base.OnMouseLeave(e);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (openBtn == null || moreBtn == null || activeSwitch == null) return; // layout runs before the buttons exist
        int pad = Theme.S(16);
        moreBtn.Location = new Point(Width - pad - moreBtn.Width, (Height - moreBtn.Height) / 2);
        openBtn.Location = new Point(moreBtn.Left - Theme.S(8) - openBtn.Width, (Height - openBtn.Height) / 2);
        activeSwitch.Location = new Point(openBtn.Left - Theme.S(16) - activeSwitch.Width, (Height - activeSwitch.Height) / 2);
    }

    static string Initial(string name)
    {
        var t = (name ?? "").Trim();
        return t.Length == 0 ? "?" : char.ToUpperInvariant(t[0]).ToString();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var fill = hover ? Theme.SurfaceHover : Theme.Surface;
        using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(22)))
        using (var b = new SolidBrush(fill))
        using (var pen = new Pen(hover ? Theme.BorderHover : Theme.Border))
        {
            g.FillPath(b, path);
            g.DrawPath(pen, path);
        }

        // avatar
        int av = Theme.S(40);
        int ax = Theme.S(16);
        int ay = (Height - av) / 2;
        using (var b = new SolidBrush(Profile.Color)) g.FillEllipse(b, ax, ay, av, av);
        TextRenderer.DrawText(g, Initial(Profile.Name), Theme.Avatar, new Rectangle(ax, ay, av, av), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (Running)
        {
            int d = Theme.S(12);
            var dot = new Rectangle(ax + av - d + Theme.S(1), ay + av - d + Theme.S(1), d, d);
            var ring = Rectangle.Inflate(dot, Theme.S(2), Theme.S(2));
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, ring);
            using (var b = new SolidBrush(Theme.Green)) g.FillEllipse(b, dot);
        }

        // text
        var flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        int tx = ax + av + Theme.S(14);
        int tw = Math.Max(Theme.S(10), activeSwitch.Left - Theme.S(12) - tx);
        int nameH = TextRenderer.MeasureText(g, "Ag", Theme.Name, new Size(int.MaxValue, int.MaxValue), flags).Height;
        int subH = TextRenderer.MeasureText(g, "Ag", Theme.Small, new Size(int.MaxValue, int.MaxValue), flags).Height;
        int gap = Theme.S(3);
        int ty = (Height - nameH - gap - subH) / 2;

        TextRenderer.DrawText(g, Profile.Name, Theme.Name, new Rectangle(tx, ty, tw, nameH), Theme.Text,
            flags | TextFormatFlags.EndEllipsis);

        if (Profile.IsTemplate)
        {
            const string badge = "Template";
            int nw = TextRenderer.MeasureText(g, Profile.Name, Theme.Name, new Size(int.MaxValue, nameH), flags).Width;
            var bs = TextRenderer.MeasureText(g, badge, Theme.Small, new Size(int.MaxValue, subH), flags);
            int bx = tx + nw + Theme.S(8);
            var br = new Rectangle(bx, ty + (nameH - bs.Height - Theme.S(4)) / 2, bs.Width + Theme.S(12), bs.Height + Theme.S(4));
            if (br.Right < tx + tw)
            {
                using (var path = Theme.Round(br, br.Height / 2f))
                using (var pen = new Pen(Theme.Accent))
                    g.DrawPath(pen, path);
                TextRenderer.DrawText(g, badge, Theme.Small, br, Theme.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }

        int sy = ty + nameH + gap;
        var status = Running ? "Berjalan" : "Tidak berjalan";
        var statusColor = Running ? Theme.Green : Theme.Muted;
        int sw = TextRenderer.MeasureText(g, status, Theme.Small, new Size(int.MaxValue, subH), flags).Width;
        TextRenderer.DrawText(g, status, Theme.Small, new Rectangle(tx, sy, Math.Min(sw, tw), subH), statusColor,
            flags | TextFormatFlags.EndEllipsis);
        if (sw < tw)
        {
            var detail = Email ?? (Profile.IsDefault ? "Belum login" : Profile.Subtitle);
            TextRenderer.DrawText(g, "  ·  " + detail, Theme.Small,
                new Rectangle(tx + sw, sy, tw - sw, subH), Email != null ? Theme.Text : Theme.Muted, flags | TextFormatFlags.EndEllipsis);
        }
    }
}

class DarkColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
    public override Color MenuBorder { get { return Theme.Border; } }
    public override Color MenuItemBorder { get { return Theme.SurfaceHover; } }
    public override Color MenuItemSelected { get { return Theme.SurfaceHover; } }
    public override Color MenuItemSelectedGradientBegin { get { return Theme.SurfaceHover; } }
    public override Color MenuItemSelectedGradientEnd { get { return Theme.SurfaceHover; } }
    public override Color MenuItemPressedGradientBegin { get { return Theme.SurfaceHover; } }
    public override Color MenuItemPressedGradientEnd { get { return Theme.SurfaceHover; } }
    public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
    public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
    public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
    public override Color SeparatorDark { get { return Theme.Border; } }
    public override Color SeparatorLight { get { return Theme.Surface; } }
}

class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (!e.Item.Enabled) e.TextColor = Theme.Muted;
        else if ("danger".Equals(e.Item.Tag)) e.TextColor = Theme.Danger;
        else e.TextColor = Theme.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.Muted;
        base.OnRenderArrow(e);
    }
}

// ---------------------------------------------------------------- dialog

class DarkDialog : Form
{
    readonly TextBox input;
    readonly Panel inputBox;
    readonly Label hintLbl;
    readonly Func<string, string> hint, validate;

    DarkDialog(string title, string message, string okText, bool danger, bool showCancel,
               bool withInput, string initial, Func<string, string> hint, Func<string, string> validate)
    {
        this.hint = hint;
        this.validate = validate;
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;

        int w = Theme.S(420), pad = Theme.S(24), inner = w - 2 * pad, y = Theme.S(20);

        int msgH = TextRenderer.MeasureText(message, Theme.Body, new Size(inner, int.MaxValue), TextFormatFlags.WordBreak).Height;
        Controls.Add(new Label { Text = message, Location = new Point(pad, y), Size = new Size(inner, msgH), ForeColor = Theme.Text });
        y += msgH + Theme.S(14);

        if (withInput)
        {
            inputBox = new Panel { Location = new Point(pad, y), Size = new Size(inner, Theme.S(38)), BackColor = Theme.Surface };
            input = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.Input,
                Text = initial ?? ""
            };
            input.Location = new Point(Theme.S(10), (inputBox.Height - input.PreferredHeight) / 2);
            input.Width = inner - Theme.S(20);
            inputBox.Controls.Add(input);
            inputBox.Paint += (s, e) =>
            {
                using (var pen = new Pen(input.Focused ? Theme.BorderHover : Theme.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, inputBox.Width - 1, inputBox.Height - 1);
            };
            input.GotFocus += (s, e) => inputBox.Invalidate();
            input.LostFocus += (s, e) => inputBox.Invalidate();
            input.TextChanged += (s, e) => UpdateHint();
            Controls.Add(inputBox);
            y += inputBox.Height + Theme.S(6);

            hintLbl = new Label { Location = new Point(pad, y), Size = new Size(inner, Theme.S(20)), ForeColor = Theme.Muted, Font = Theme.Small };
            Controls.Add(hintLbl);
            y += Theme.S(22);
            UpdateHint();
        }

        y += Theme.S(10);
        int bw = Theme.S(100), bh = Theme.S(36);
        var okBtn = danger
            ? new FlatButton { Text = okText, BaseColor = Theme.Danger, HoverColor = Theme.DangerHover, ForeColor = Color.White }
            : FlatButton.Primary(okText);
        okBtn.BackColor = Theme.Bg;
        okBtn.Bounds = new Rectangle(w - pad - bw, y, bw, bh);
        okBtn.Click += (s, e) => Accept();
        Controls.Add(okBtn);
        if (showCancel)
        {
            var cancelBtn = FlatButton.Secondary("Batal");
            cancelBtn.BackColor = Theme.Bg;
            cancelBtn.Bounds = new Rectangle(okBtn.Left - Theme.S(8) - bw, y, bw, bh);
            cancelBtn.Click += (s, e) => DialogResult = DialogResult.Cancel;
            Controls.Add(cancelBtn);
        }
        ClientSize = new Size(w, y + bh + Theme.S(20));
    }

    void UpdateHint()
    {
        if (hintLbl == null) return;
        hintLbl.ForeColor = Theme.Muted;
        hintLbl.Text = hint == null ? "" : hint(input.Text.Trim());
    }

    void Accept()
    {
        if (input != null && validate != null)
        {
            var error = validate(input.Text.Trim());
            if (error != null)
            {
                hintLbl.ForeColor = Theme.Danger;
                hintLbl.Text = error;
                input.Focus();
                return;
            }
        }
        DialogResult = DialogResult.OK;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter) { Accept(); return true; }
        if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle, Theme.Bg);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (input != null)
        {
            input.Focus();
            input.SelectAll();
        }
    }

    public static string Prompt(IWin32Window owner, string title, string message, string initial, string okText,
                                Func<string, string> hint, Func<string, string> validate)
    {
        using (var d = new DarkDialog(title, message, okText, false, true, true, initial, hint, validate))
        {
            return d.ShowDialog(owner) == DialogResult.OK ? d.input.Text.Trim() : null;
        }
    }

    public static bool Confirm(IWin32Window owner, string title, string message, string okText, bool danger)
    {
        using (var d = new DarkDialog(title, message, okText, danger, true, false, null, null, null))
        {
            return d.ShowDialog(owner) == DialogResult.OK;
        }
    }

    public static void Alert(IWin32Window owner, string title, string message)
    {
        using (var d = new DarkDialog(title, message, "OK", false, false, false, null, null, null))
        {
            d.ShowDialog(owner);
        }
    }
}

class CopyDialog : Form
{
    readonly ComboBox source;
    readonly DarkCheck mcp, prefs, toggles, presets, extensions;
    readonly List<Profile> sources;

    public Profile Source { get { return source.SelectedIndex < 0 ? null : sources[source.SelectedIndex]; } }

    public CopyOptions Options
    {
        get
        {
            return new CopyOptions
            {
                Mcp = mcp.Checked,
                Prefs = prefs.Checked,
                ToolToggles = toggles.Checked,
                Presets = presets.Checked,
                Extensions = extensions.Checked
            };
        }
    }

    public CopyDialog(Profile target, List<Profile> all)
    {
        Text = "Salin setup";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;

        int w = Theme.S(440), pad = Theme.S(24), inner = w - 2 * pad, y = Theme.S(20);

        Controls.Add(new Label
        {
            Text = "Salin setup ke \"" + target.Name + "\"",
            Font = Theme.Name,
            Location = new Point(pad, y),
            Size = new Size(inner, Theme.S(24))
        });
        y += Theme.S(36);

        Controls.Add(new Label { Text = "Ambil dari", ForeColor = Theme.Muted, Font = Theme.Small, Location = new Point(pad, y), Size = new Size(inner, Theme.S(18)) });
        y += Theme.S(22);
        sources = all.Where(p => p != target).ToList();
        source = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Font = Theme.Input,
            Location = new Point(pad, y),
            Width = inner
        };
        foreach (var p in sources) source.Items.Add(p.IsTemplate ? p.Name + "  (template)" : p.Name);
        var template = sources.FirstOrDefault(p => p.IsTemplate);
        source.SelectedIndex = sources.Count == 0 ? -1 : Math.Max(0, sources.IndexOf(template ?? sources.FirstOrDefault(p => p.IsDefault)));
        Controls.Add(source);
        y += source.Height + Theme.S(18);

        Controls.Add(new Label { Text = "Yang disalin", ForeColor = Theme.Muted, Font = Theme.Small, Location = new Point(pad, y), Size = new Size(inner, Theme.S(18)) });
        y += Theme.S(24);
        mcp = AddCheck("MCP server (mode chat)", pad, inner, ref y);
        prefs = AddCheck("Preferensi aplikasi (Cowork, browser, izin, dll.)", pad, inner, ref y);
        toggles = AddCheck("Status on/off tool MCP", pad, inner, ref y);
        presets = AddCheck("Preset sesi Claude Code", pad, inner, ref y);
        extensions = AddCheck("Extension (.dxt)", pad, inner, ref y);

        y += Theme.S(8);
        var note = "Login tidak ikut disalin. File lama di tujuan dibackup ke folder launcher-backup.\n" +
                   "Skill, memory, CLAUDE.md dan MCP Claude Code sudah sama di semua instance.";
        int noteH = TextRenderer.MeasureText(note, Theme.Small, new Size(inner, int.MaxValue), TextFormatFlags.WordBreak).Height;
        Controls.Add(new Label { Text = note, ForeColor = Theme.Muted, Font = Theme.Small, Location = new Point(pad, y), Size = new Size(inner, noteH) });
        y += noteH + Theme.S(18);

        int bw = Theme.S(100), bh = Theme.S(36);
        var okBtn = FlatButton.Primary("Salin");
        okBtn.BackColor = Theme.Bg;
        okBtn.Bounds = new Rectangle(w - pad - bw, y, bw, bh);
        okBtn.Click += (s, e) => { if (Source != null) DialogResult = DialogResult.OK; };
        var cancelBtn = FlatButton.Secondary("Batal");
        cancelBtn.BackColor = Theme.Bg;
        cancelBtn.Bounds = new Rectangle(okBtn.Left - Theme.S(8) - bw, y, bw, bh);
        cancelBtn.Click += (s, e) => DialogResult = DialogResult.Cancel;
        Controls.Add(okBtn);
        Controls.Add(cancelBtn);
        ClientSize = new Size(w, y + bh + Theme.S(20));
    }

    DarkCheck AddCheck(string text, int x, int width, ref int y)
    {
        var c = new DarkCheck(text) { Location = new Point(x, y), Width = width };
        Controls.Add(c);
        y += c.Height + Theme.S(4);
        return c;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle, Theme.Bg);
    }
}

// ---------------------------------------------------------------- main window

partial class LauncherForm : Form
{
    readonly List<Profile> profiles;
    readonly Panel header;
    readonly Label titleLbl, summaryLbl, emptyLbl;
    readonly FlatButton addBtn, arrangeBtn, layoutBtn, menuBtn;
    readonly LayoutSettings layout = LayoutSettings.Load();
    bool arranging;
    readonly FlowLayoutPanel cardsPanel;
    readonly List<ProfileCard> cards = new List<ProfileCard>();
    readonly DarkRenderer renderer = new DarkRenderer();
    HashSet<string> running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, string> emails = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // data dir -> email
    ContextMenuStrip menu;
    bool polling;

    public LauncherForm(bool autostart)
    {
        Text = "Claude Launcher";
        ClientSize = new Size(Theme.S(700), Theme.S(560));
        MinimumSize = new Size(Theme.S(660), Theme.S(360));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        DoubleBuffered = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // header
        header = new Panel { Dock = DockStyle.Top, Height = Theme.S(92), BackColor = Theme.Bg };
        titleLbl = new Label { Text = "Claude Launcher", Font = Theme.Title, ForeColor = Theme.Text, AutoSize = true, Location = new Point(Theme.S(22), Theme.S(20)) };
        summaryLbl = new Label { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Location = new Point(Theme.S(25), Theme.S(56)) };
        addBtn = FlatButton.Primary("+  Tambah");
        addBtn.Size = new Size(Theme.S(108), Theme.S(36));
        addBtn.BackColor = Theme.Bg;
        addBtn.Click += (s, e) => AddProfile();
        arrangeBtn = FlatButton.Secondary("Buka & rapikan");
        arrangeBtn.BaseColor = Theme.Bg;
        arrangeBtn.BackColor = Theme.Bg;
        arrangeBtn.Size = new Size(Theme.S(164), Theme.S(36));
        arrangeBtn.Click += (s, e) => OpenAndArrange();
        layoutBtn = FlatButton.Secondary(""); // Segoe MDL2 "Settings" glyph
        layoutBtn.Font = new Font("Segoe MDL2 Assets", 11f);
        layoutBtn.BaseColor = Theme.Bg;
        layoutBtn.BackColor = Theme.Bg;
        layoutBtn.Size = new Size(Theme.S(36), Theme.S(36));
        layoutBtn.Click += (s, e) => EditLayout();
        menuBtn = FlatButton.Secondary(""); // Segoe MDL2 "GlobalNavButton" glyph
        menuBtn.Font = new Font("Segoe MDL2 Assets", 11f);
        menuBtn.BaseColor = Theme.Bg;
        menuBtn.BackColor = Theme.Bg;
        menuBtn.Size = new Size(Theme.S(36), Theme.S(36));
        menuBtn.Click += (s, e) => ShowAppMenu(menuBtn);
        var tips = new ToolTip();
        tips.SetToolTip(layoutBtn, "Atur tata letak jendela");
        tips.SetToolTip(menuBtn, "Preset, tutup semua, dan pengaturan");
        tips.SetToolTip(arrangeBtn, "Buka instance yang aktif lalu susun jendelanya");
        header.Controls.AddRange(new Control[] { titleLbl, summaryLbl, addBtn, arrangeBtn, layoutBtn, menuBtn });
        header.Resize += (s, e) => LayoutHeader();

        // cards
        cardsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Theme.Bg,
            Padding = new Padding(Theme.S(24), Theme.S(4), Theme.S(24), Theme.S(16))
        };
        typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(cardsPanel, true, null);
        cardsPanel.HandleCreated += (s, e) => Native.DarkScrollbars(cardsPanel.Handle);
        cardsPanel.Resize += (s, e) => ResizeCards();

        emptyLbl = new Label
        {
            Text = "Belum ada instance. Klik \"+ Tambah\" untuk membuat.",
            ForeColor = Theme.Muted,
            AutoSize = true,
            Margin = new Padding(0, Theme.S(24), 0, 0)
        };

        var footer = new Label
        {
            Dock = DockStyle.Bottom,
            Height = Theme.S(34),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.Muted,
            Font = Theme.Small,
            Text = "Klik dua kali kartu untuk membuka  ·  Klik kanan untuk opsi"
        };

        Controls.Add(cardsPanel);
        Controls.Add(footer);
        Controls.Add(header);

        profiles = Store.Load();
        BuildCards();

        var timer = new System.Windows.Forms.Timer { Interval = 2500 };
        timer.Tick += (s, e) => Poll();
        timer.Start();

        InitFeatures(autostart);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle, Theme.Bg);
        RegisterHotkeys();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LayoutHeader();
        Poll();
    }

    void LayoutHeader()
    {
        int pad = Theme.S(24);
        int top = Theme.S(26);
        addBtn.Location = new Point(header.Width - pad - addBtn.Width, top);
        arrangeBtn.Location = new Point(addBtn.Left - Theme.S(8) - arrangeBtn.Width, top);
        layoutBtn.Location = new Point(arrangeBtn.Left - Theme.S(8) - layoutBtn.Width, top);
        menuBtn.Location = new Point(layoutBtn.Left - Theme.S(8) - menuBtn.Width, top);
    }

    // ---------- cards ----------

    void BuildCards()
    {
        cardsPanel.SuspendLayout();
        foreach (Control c in cardsPanel.Controls.Cast<Control>().ToList())
        {
            cardsPanel.Controls.Remove(c);
            if (c != emptyLbl) c.Dispose();
        }
        cards.Clear();
        foreach (var p in profiles)
        {
            var card = new ProfileCard(p, layout.Includes(p), Launch, ShowMenu, SetActive);
            card.SetRunning(running.Contains(Claude.Key(p)));
            card.SetEmail(EmailFor(p));
            cards.Add(card);
            cardsPanel.Controls.Add(card);
        }
        if (profiles.Count == 0) cardsPanel.Controls.Add(emptyLbl);
        ResizeCards();
        cardsPanel.ResumeLayout();
        UpdateSummary();
    }

    void ResizeCards()
    {
        int w = cardsPanel.ClientSize.Width - cardsPanel.Padding.Horizontal - 1;
        if (w < Theme.S(200)) return;
        foreach (var c in cards) c.Width = w;
    }

    void UpdateSummary()
    {
        int on = cards.Count(c => c.Running);
        int active = profiles.Count(p => layout.Includes(p));
        summaryLbl.Text = profiles.Count + " instance  ·  " + on + " berjalan  ·  " + active + " aktif";
        if (!arranging) arrangeBtn.Text = "Buka & rapikan (" + active + ")";
    }

    // The card switch and the layout editor's instance checks share layout.Ids.
    void SetActive(Profile p, bool active)
    {
        var ids = layout.Ids ?? profiles.Select(x => x.Id).ToList();
        ids.RemoveAll(id => string.Equals(id, p.Id, StringComparison.OrdinalIgnoreCase));
        if (active) ids.Add(p.Id);
        layout.Ids = profiles.Where(x => ids.Contains(x.Id, StringComparer.OrdinalIgnoreCase)).Select(x => x.Id).ToList();
        layout.Save();
        UpdateSummary();
    }

    void Poll()
    {
        if (polling) return;
        polling = true;
        var dirs = profiles.Select(p => p.StorageDir).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Task.Run(() => Tuple.Create(Claude.RunningKeys(), dirs.ToDictionary(d => d, AccountInfo.Email, StringComparer.OrdinalIgnoreCase))).ContinueWith(t =>
        {
            polling = false;
            if (IsDisposed) return;
            running = t.Result.Item1;
            emails = t.Result.Item2;
            foreach (var c in cards)
            {
                c.SetRunning(running.Contains(Claude.Key(c.Profile)));
                c.SetEmail(EmailFor(c.Profile));
            }
            UpdateSummary();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    string EmailFor(Profile p)
    {
        string email;
        return emails.TryGetValue(p.StorageDir, out email) ? email : null;
    }

    void PollSoon()
    {
        var t = new System.Windows.Forms.Timer { Interval = 1500 };
        t.Tick += (s, e) =>
        {
            t.Stop();
            t.Dispose();
            Poll();
        };
        t.Start();
    }

    // ---------- actions ----------

    void Launch(Profile p)
    {
        try
        {
            Claude.Launch(p);
            PollSoon();
        }
        catch (Exception ex)
        {
            DarkDialog.Alert(this, "Gagal membuka", "Gagal membuka \"" + p.Name + "\".\n\n" + ex.Message);
        }
    }

    void EditLayout()
    {
        using (var d = new LayoutEditorForm(layout, profiles))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
        }
        layout.Save();
        BuildCards();
    }

    // Opens the chosen instances that are not running yet, waits for their windows, then tiles them.
    async void OpenAndArrange()
    {
        if (arranging) return;
        var targets = profiles.Where(p => layout.Includes(p)).ToList();
        if (targets.Count == 0)
        {
            DarkDialog.Alert(this, "Belum ada instance", "Pilih instance yang mau dirapikan lewat tombol pengaturan di sebelah \"Buka & rapikan\".");
            return;
        }
        arranging = true;
        try
        {
            var monitor = WindowArranger.Find(layout.Monitor);
            var procs = await Task.Run(() => Claude.RunningProcesses());
            bool launched = false;
            foreach (var t in targets)
            {
                // Running but hidden in the tray: launching again makes Claude show its window.
                int pid;
                if (procs.TryGetValue(Claude.Key(t), out pid) && WindowArranger.MainWindow(pid) != IntPtr.Zero) continue;
                Launch(t);
                launched = true;
            }

            var deadline = DateTime.Now.AddSeconds(30);
            List<IntPtr> windows;
            while (true)
            {
                procs = await Task.Run(() => Claude.RunningProcesses());
                var snapshot = procs;
                windows = targets.Select(t =>
                {
                    int pid;
                    return snapshot.TryGetValue(Claude.Key(t), out pid) ? WindowArranger.MainWindow(pid) : IntPtr.Zero;
                }).ToList();
                int found = windows.Count(h => h != IntPtr.Zero);
                arrangeBtn.Text = "Menunggu " + found + "/" + targets.Count;
                if (found == targets.Count || DateTime.Now > deadline) break;
                await Task.Delay(700);
            }

            var ready = windows.Where(h => h != IntPtr.Zero).ToList();
            arrangeBtn.Text = "Merapikan...";
            // Claude restores its own saved window size on startup; arrange after that settles.
            var zones = Layouts.Resolve(layout.Layout, ready.Count, Layouts.LoadCustom());
            if (launched) await Task.Delay(1500);
            WindowArranger.Arrange(ready, monitor, zones, layout.Gap);
            if (launched)
            {
                await Task.Delay(1500);
                WindowArranger.Arrange(ready, monitor, zones, layout.Gap);
            }

            if (ready.Count < targets.Count)
            {
                DarkDialog.Alert(this, "Sebagian belum muncul",
                    ready.Count + " dari " + targets.Count + " jendela dirapikan. Sisanya belum muncul dalam 30 detik; klik \"Buka & rapikan\" lagi setelah terbuka.");
            }
        }
        catch (Exception ex)
        {
            DarkDialog.Alert(this, "Gagal merapikan", ex.Message);
        }
        finally
        {
            arranging = false;
            UpdateSummary();
            Poll();
        }
    }

    static string ValidateName(string name)
    {
        if (name.Length == 0) return "Nama tidak boleh kosong.";
        if (name.Length > 40) return "Maksimal 40 karakter.";
        if (name.Contains("|")) return "Karakter | tidak boleh dipakai.";
        return null;
    }

    void AddProfile()
    {
        var template = Store.Template(profiles);
        var message = "Beri nama instance baru. Login dan datanya tersimpan terpisah dari instance lain.";
        if (template != null) message += "\n\nSetup disalin otomatis dari template \"" + template.Name + "\".";
        var name = DarkDialog.Prompt(this, "Tambah instance", message, "", "Tambah",
            s => s.Length == 0 ? "" : "Folder data: ~\\.claude-" + Store.NewId(s, profiles),
            ValidateName);
        if (name == null) return;
        var profile = new Profile
        {
            Id = Store.NewId(name, profiles),
            Name = name,
            Color = Theme.Palette[profiles.Count % Theme.Palette.Length]
        };
        profiles.Add(profile);
        Save();

        if (template == null) return;
        try
        {
            SetupCopier.Copy(template, profile, new CopyOptions());
        }
        catch (Exception ex)
        {
            DarkDialog.Alert(this, "Template gagal disalin",
                "Instance \"" + name + "\" sudah dibuat, tapi setup dari template gagal disalin.\n\n" + ex.Message);
        }
    }

    void CopySetup(Profile target)
    {
        if (running.Contains(Claude.Key(target)))
        {
            DarkDialog.Alert(this, "Tutup dulu",
                "\"" + target.Name + "\" sedang berjalan. Tutup instance itu dulu, karena Claude menimpa setting-nya saat keluar.");
            return;
        }
        Profile source;
        CopyOptions options;
        using (var d = new CopyDialog(target, profiles))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            source = d.Source;
            options = d.Options;
        }
        try
        {
            DarkDialog.Alert(this, "Setup disalin", SetupCopier.Copy(source, target, options));
        }
        catch (Exception ex)
        {
            DarkDialog.Alert(this, "Gagal menyalin", ex.Message);
        }
    }

    void ToggleTemplate(Profile p)
    {
        bool make = !p.IsTemplate;
        foreach (var x in profiles) x.IsTemplate = false;
        p.IsTemplate = make;
        Save();
    }

    void Rename(Profile p)
    {
        var name = DarkDialog.Prompt(this, "Ganti nama",
            "Nama tampilan untuk instance ini. Folder data dan login tidak berubah.",
            p.Name, "Simpan", null, ValidateName);
        if (name == null) return;
        p.Name = name;
        Save();
    }

    void SetColor(Profile p, Color c)
    {
        p.Color = c;
        Save();
    }

    void MoveProfile(Profile p, int delta)
    {
        int i = profiles.IndexOf(p);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= profiles.Count) return;
        profiles[i] = profiles[j];
        profiles[j] = p;
        Save();
    }

    void Remove(Profile p)
    {
        var where = p.IsDefault ? "Profil bawaan aplikasi" : "Folder ~\\.claude-" + p.Id;
        if (!DarkDialog.Confirm(this, "Hapus dari daftar",
            "Hapus \"" + p.Name + "\" dari launcher?\n\n" + where + " tidak dihapus, jadi login dan datanya tetap ada.",
            "Hapus", true)) return;
        profiles.Remove(p);
        Save();
    }

    void OpenFolder(Profile p)
    {
        var dir = p.StorageDir;
        if (!Directory.Exists(dir))
        {
            DarkDialog.Alert(this, "Folder belum ada",
                "Folder dibuat otomatis saat instance ini pertama kali dibuka.\n\n" + dir);
            return;
        }
        Process.Start("explorer.exe", "\"" + dir + "\"");
    }

    void Save()
    {
        Store.Save(profiles);
        BuildCards();
    }

    // ---------- menu ----------

    ToolStripMenuItem Item(string text, Action onClick)
    {
        var item = new ToolStripMenuItem(text) { Padding = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
        item.Click += (s, e) => onClick();
        return item;
    }

    void ShowMenu(ProfileCard card, Control anchor)
    {
        var p = card.Profile;
        int index = profiles.IndexOf(p);
        if (menu != null) menu.Dispose();
        menu = new ContextMenuStrip { Renderer = renderer, Font = Theme.Body, ShowImageMargin = false };

        menu.Items.Add(Item(card.Running ? "Tampilkan" : "Buka", () => Launch(p)));
        var close = Item("Tutup instance", () => CloseInstance(p));
        close.Enabled = card.Running;
        menu.Items.Add(close);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Ganti nama...", () => Rename(p)));
        var copy = Item("Salin setup dari...", () => CopySetup(p));
        copy.Enabled = profiles.Count > 1;
        menu.Items.Add(copy);
        menu.Items.Add(Item(p.IsTemplate ? "Berhenti jadi template" : "Jadikan template", () => ToggleTemplate(p)));

        var colors = new ToolStripMenuItem("Warna") { Padding = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
        colors.DropDown.Renderer = renderer;
        colors.DropDown.Font = Theme.Body;
        for (int i = 0; i < Theme.Palette.Length; i++)
        {
            var c = Theme.Palette[i];
            var swatch = Item(Theme.PaletteNames[i], () => SetColor(p, c));
            swatch.Image = Theme.Swatch(c);
            if (c.ToArgb() == p.Color.ToArgb()) swatch.Text += "  ✓";
            colors.DropDownItems.Add(swatch);
        }
        menu.Items.Add(colors);

        var up = Item("Pindah ke atas", () => MoveProfile(p, -1));
        up.Enabled = index > 0;
        var down = Item("Pindah ke bawah", () => MoveProfile(p, 1));
        down.Enabled = index < profiles.Count - 1;
        menu.Items.Add(up);
        menu.Items.Add(down);
        menu.Items.Add(Item("Buka folder data", () => OpenFolder(p)));
        menu.Items.Add(new ToolStripSeparator());
        var remove = Item("Hapus dari daftar", () => Remove(p));
        remove.Tag = "danger";
        menu.Items.Add(remove);

        if (anchor != null) menu.Show(anchor, new Point(0, anchor.Height + Theme.S(4)));
        else menu.Show(Cursor.Position);
    }
}
