// DualCursor (Windows) — her ekrana ayrı bir imleç.
// Gerçek imleç ClipCursor ile aktif ekrana kilitlenir; diğer ekranlarda
// "hayalet" imleç en son bırakılan yerde durur. Kısayolla anında yer değiştirirler.
// MIT License

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DualCursor
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool ClipCursor(ref RECT r);
        [DllImport("user32.dll", EntryPoint = "ClipCursor")] public static extern bool ClipCursorNone(IntPtr p);
        [DllImport("user32.dll")] public static extern bool GetClipCursor(out RECT r);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);

        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

        public static RECT ToRect(Rectangle b) => new RECT { Left = b.Left, Top = b.Top, Right = b.Right, Bottom = b.Bottom };
    }

    // ---------- Ayarlar (%APPDATA%\DualCursor\config.json) ----------

    class HotkeyConfig
    {
        public string key { get; set; } = "space";
        public string[] modifiers { get; set; } = new[] { "ctrl", "alt" };

        public static string Path => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DualCursor", "config.json");

        public static HotkeyConfig Load()
        {
            try
            {
                if (File.Exists(Path))
                    return JsonSerializer.Deserialize<HotkeyConfig>(File.ReadAllText(Path)) ?? new HotkeyConfig();
            }
            catch { /* bozuk dosya: varsayılana dön */ }

            var cfg = new HotkeyConfig();
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                File.WriteAllText(Path, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
            return cfg;
        }

        public Keys ParseKey()
        {
            var k = key.Trim().ToLowerInvariant();
            if (k.Length == 1 && char.IsDigit(k[0])) return Keys.D0 + (k[0] - '0');
            switch (k)
            {
                case "grave": return Keys.Oemtilde;
                case "escape": return Keys.Escape;
                case "return": return Keys.Return;
            }
            return Enum.TryParse(k, true, out Keys parsed) ? parsed : Keys.Space;
        }

        public uint ParseModifiers()
        {
            uint m = Native.MOD_NOREPEAT;
            foreach (var mod in modifiers ?? Array.Empty<string>())
            {
                switch (mod.Trim().ToLowerInvariant())
                {
                    case "alt": case "option": m |= Native.MOD_ALT; break;
                    case "ctrl": case "control": m |= Native.MOD_CONTROL; break;
                    case "shift": m |= Native.MOD_SHIFT; break;
                    case "win": case "cmd": case "command": m |= Native.MOD_WIN; break;
                }
            }
            return m;
        }

        public string Label => string.Join("+", (modifiers ?? Array.Empty<string>()).Append(key));
    }

    // ---------- Hayalet imleç penceresi ----------

    class GhostForm : Form
    {
        public Point? Ghost;   // forma göre yerel konum (null = gizli)

        public GhostForm(Rectangle bounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = bounds;
            TopMost = true;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            Opacity = 0.85;
            DoubleBuffered = true;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000,
                          WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Ghost is Point p)
            {
                var c = Cursors.Default;
                c.Draw(e.Graphics, new Rectangle(p.X - c.HotSpot.X, p.Y - c.HotSpot.Y, c.Size.Width, c.Size.Height));
            }
        }
    }

    // ---------- Global kısayol alıcısı ----------

    class HotkeyWindow : NativeWindow, IDisposable
    {
        public event Action Pressed;
        public HotkeyWindow() { CreateHandle(new CreateParams()); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY) Pressed?.Invoke();
            base.WndProc(ref m);
        }

        public void Dispose() { DestroyHandle(); }
    }

    // ---------- Ana uygulama ----------

    class DualCursorApp : ApplicationContext
    {
        readonly List<(Screen screen, GhostForm form)> screens = new();
        readonly Dictionary<string, Point> saved = new();
        string activeName;

        readonly HotkeyWindow hotkey = new();
        readonly Timer clipTimer = new() { Interval = 100 };
        readonly NotifyIcon tray;
        readonly HotkeyConfig cfg = HotkeyConfig.Load();

        public DualCursorApp()
        {
            Rebuild();

            hotkey.Pressed += SwitchNext;
            if (!Native.RegisterHotKey(hotkey.Handle, 1, cfg.ParseModifiers(), (uint)cfg.ParseKey()))
                MessageBox.Show($"Kısayol kaydedilemedi: {cfg.Label}\nBaşka bir uygulama kullanıyor olabilir.",
                                "DualCursor", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            // Windows bazı durumlarda (pencere sürükleme, odak değişimi) kilidi sıfırlar; tekrar uygula.
            clipTimer.Tick += (_, __) => EnforceClip();
            clipTimer.Start();

            var menu = new ContextMenuStrip();
            menu.Items.Add($"Geçiş: {cfg.Label}").Enabled = false;
            menu.Items.Add("Ayar dosyasını aç", null, (_, __) =>
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(HotkeyConfig.Path) { UseShellExecute = true }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Çıkış", null, (_, __) => ExitThread());

            tray = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = $"DualCursor — {cfg.Label}",
                ContextMenuStrip = menu,
                Visible = true
            };

            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        }

        void OnDisplayChanged(object s, EventArgs e) => Rebuild();

        Screen Active => screens.Select(x => x.screen).FirstOrDefault(s => s.DeviceName == activeName);

        void Rebuild()
        {
            foreach (var (_, f) in screens) f.Close();
            screens.Clear();

            foreach (var s in Screen.AllScreens)
            {
                var f = new GhostForm(s.Bounds);
                f.Show();
                f.Bounds = s.Bounds;   // DPI ölçeklemesine karşı tekrar ayarla
                screens.Add((s, f));
                if (!saved.ContainsKey(s.DeviceName))
                    saved[s.DeviceName] = Center(s.Bounds);
            }

            var names = new HashSet<string>(screens.Select(x => x.screen.DeviceName));
            foreach (var k in saved.Keys.Where(k => !names.Contains(k)).ToList()) saved.Remove(k);

            activeName = Screen.FromPoint(Cursor.Position).DeviceName;
            ApplyClip();
            RefreshGhosts();
        }

        static Point Center(Rectangle r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);

        void SwitchNext()
        {
            if (screens.Count < 2) return;

            saved[activeName] = Cursor.Position;
            int idx = screens.FindIndex(x => x.screen.DeviceName == activeName);
            var next = screens[(idx + 1) % screens.Count].screen;
            activeName = next.DeviceName;

            var target = saved.TryGetValue(next.DeviceName, out var p) ? p : Center(next.Bounds);
            Native.ClipCursorNone(IntPtr.Zero);
            Native.SetCursorPos(target.X, target.Y);
            ApplyClip();
            RefreshGhosts();
        }

        void ApplyClip()
        {
            var a = Active;
            if (a == null) return;
            var r = Native.ToRect(a.Bounds);
            Native.ClipCursor(ref r);
        }

        void EnforceClip()
        {
            var a = Active;
            if (a == null || screens.Count < 2) return;
            Native.GetClipCursor(out var cur);
            var want = Native.ToRect(a.Bounds);
            if (cur.Left != want.Left || cur.Top != want.Top || cur.Right != want.Right || cur.Bottom != want.Bottom)
                Native.ClipCursor(ref want);
        }

        void RefreshGhosts()
        {
            foreach (var (s, f) in screens)
            {
                if (s.DeviceName == activeName) f.Ghost = null;
                else if (saved.TryGetValue(s.DeviceName, out var p))
                    f.Ghost = new Point(p.X - s.Bounds.Left, p.Y - s.Bounds.Top);
                f.Invalidate();
            }
        }

        protected override void ExitThreadCore()
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            clipTimer.Stop();
            Native.UnregisterHotKey(hotkey.Handle, 1);
            Native.ClipCursorNone(IntPtr.Zero);
            hotkey.Dispose();
            tray.Visible = false;
            tray.Dispose();
            foreach (var (_, f) in screens) f.Close();
            base.ExitThreadCore();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new DualCursorApp());
        }
    }
}
