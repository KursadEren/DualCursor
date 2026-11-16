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
                case "grave": case "`": return Keys.Oemtilde;
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

        public string Label => Describe(ParseKey(), modifiers);

        public void Save()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static string KeyName(Keys k)
        {
            if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
            if (k == Keys.Oemtilde) return "`";
            return k.ToString();
        }

        public static string Describe(Keys key, IEnumerable<string> mods)
        {
            var parts = new List<string>();
            var set = new HashSet<string>((mods ?? Array.Empty<string>()).Select(m => m.ToLowerInvariant()));
            if (set.Contains("ctrl") || set.Contains("control")) parts.Add("Ctrl");
            if (set.Contains("alt") || set.Contains("option")) parts.Add("Alt");
            if (set.Contains("shift")) parts.Add("Shift");
            if (set.Contains("win") || set.Contains("cmd") || set.Contains("command")) parts.Add("Win");
            parts.Add(KeyName(key));
            return string.Join(" + ", parts);
        }
    }

    // ---------- Ayarlar penceresi ----------

    class HotkeyBox : TextBox
    {
        public Keys Key = Keys.None;
        public List<string> Mods = new();
        public event Action Changed;

        [DllImport("user32.dll")] static extern short GetKeyState(int vk);
        static bool WinDown => (GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0;

        public HotkeyBox()
        {
            ReadOnly = true;
            BackColor = SystemColors.Window;
            TextAlign = HorizontalAlignment.Center;
            Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);
            ShortcutsEnabled = false;
        }

        protected override bool IsInputKey(Keys keyData) => true;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Alt+harf gibi kombinasyonları da yakala
            Capture(keyData & Keys.KeyCode, keyData & Keys.Modifiers);
            return true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            Capture(e.KeyCode, e.Modifiers);
        }

        void Capture(Keys code, Keys modifiers)
        {
            // Sadece Ctrl/Alt/Shift/Win basılıyken bekle
            if (code is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin
                or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey
                or Keys.LMenu or Keys.RMenu or Keys.None)
            {
                Text = Preview(modifiers) + "…";
                return;
            }
            Mods = new List<string>();
            if (modifiers.HasFlag(Keys.Control)) Mods.Add("ctrl");
            if (modifiers.HasFlag(Keys.Alt)) Mods.Add("alt");
            if (modifiers.HasFlag(Keys.Shift)) Mods.Add("shift");
            if (WinDown) Mods.Add("win");
            Key = code;
            Text = HotkeyConfig.Describe(Key, Mods);
            Changed?.Invoke();
        }

        string Preview(Keys modifiers)
        {
            var p = new List<string>();
            if (modifiers.HasFlag(Keys.Control)) p.Add("Ctrl");
            if (modifiers.HasFlag(Keys.Alt)) p.Add("Alt");
            if (modifiers.HasFlag(Keys.Shift)) p.Add("Shift");
            if (WinDown) p.Add("Win");
            return p.Count == 0 ? "" : string.Join(" + ", p) + " + ";
        }
    }

    class SettingsForm : Form
    {
        readonly HotkeyBox box = new();
        readonly Label status = new();
        readonly Button save = new() { Text = "Kaydet" };
        readonly Func<Keys, uint, bool> tryRegister;
        public bool Saved;

        public SettingsForm(HotkeyConfig cfg, Func<Keys, uint, bool> tryRegister, string message = null)
        {
            this.tryRegister = tryRegister;
            Text = "DualCursor — Ayarlar";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(380, 190);
            TopMost = true;
            Font = new Font("Segoe UI", 9.5f);

            var title = new Label
            {
                Text = "Ekranlar arası geçiş kısayolu",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                AutoSize = true, Location = new Point(16, 14)
            };
            var hint = new Label
            {
                Text = "Kutuya tıklayıp istediğin tuş kombinasyonuna bas.",
                AutoSize = true, Location = new Point(16, 40), ForeColor = SystemColors.GrayText
            };

            box.Location = new Point(16, 66);
            box.Width = 348;
            box.Key = cfg.ParseKey();
            box.Mods = (cfg.modifiers ?? Array.Empty<string>()).ToList();
            box.Text = cfg.Label;
            box.Changed += CheckKey;

            status.Location = new Point(16, 104);
            status.Size = new Size(348, 36);
            status.Text = message ?? "";
            status.ForeColor = message == null ? Color.SeaGreen : Color.Firebrick;

            save.Location = new Point(196, 148);
            save.Size = new Size(80, 30);
            save.Click += (_, __) => DoSave(cfg);

            var cancel = new Button { Text = "İptal", Location = new Point(284, 148), Size = new Size(80, 30) };
            cancel.Click += (_, __) => Close();

            AcceptButton = null;   // Enter'ı kısayol olarak yakalayabilmek için
            CancelButton = null;
            Controls.AddRange(new Control[] { title, hint, box, status, save, cancel });
            Shown += (_, __) => box.Focus();
        }

        static uint ModFlags(IEnumerable<string> mods) =>
            new HotkeyConfig { modifiers = mods.ToArray() }.ParseModifiers();

        void CheckKey()
        {
            if (box.Mods.Count == 0 && !(box.Key >= Keys.F1 && box.Key <= Keys.F24))
            {
                status.ForeColor = Color.DarkOrange;
                status.Text = "Uyarı: yanında Ctrl/Alt/Shift olmadan bu tuşu her yerde kaybedersin.";
            }
            else
            {
                status.ForeColor = SystemColors.GrayText;
                status.Text = "Kaydet'e basınca hemen geçerli olur.";
            }
        }

        void DoSave(HotkeyConfig cfg)
        {
            if (box.Key == Keys.None) return;
            if (!tryRegister(box.Key, ModFlags(box.Mods)))
            {
                status.ForeColor = Color.Firebrick;
                status.Text = $"{box.Text} başka bir uygulama tarafından kullanılıyor. Başka bir kombinasyon dene.";
                return;
            }
            cfg.key = HotkeyConfig.KeyName(box.Key);
            cfg.modifiers = box.Mods.ToArray();
            try { cfg.Save(); } catch { }
            Saved = true;
            Close();
        }
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

        readonly ToolStripMenuItem labelItem = new() { Enabled = false };
        bool hotkeyRegistered;
        SettingsForm settings;

        public DualCursorApp()
        {
            Rebuild();

            hotkey.Pressed += SwitchNext;

            // Windows bazı durumlarda (pencere sürükleme, odak değişimi) kilidi sıfırlar; tekrar uygula.
            clipTimer.Tick += (_, __) => EnforceClip();
            clipTimer.Start();

            var menu = new ContextMenuStrip();
            menu.Items.Add(labelItem);
            var settingsItem = new ToolStripMenuItem("Kısayolu değiştir…", null, (_, __) => OpenSettings());
            settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);
            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Çıkış", null, (_, __) => ExitThread());

            tray = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                ContextMenuStrip = menu,
                Visible = true
            };
            tray.DoubleClick += (_, __) => OpenSettings();

            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

            hotkeyRegistered = TryRegister(cfg.ParseKey(), cfg.ParseModifiers());
            UpdateLabels();
            if (!hotkeyRegistered)
                OpenSettings($"{cfg.Label} başka bir uygulama tarafından kullanılıyor. Yeni bir kısayol seç.");
            else
                tray.ShowBalloonTip(3000, "DualCursor çalışıyor",
                    $"Geçiş: {cfg.Label}\nDeğiştirmek için bu simgeye çift tıkla.", ToolTipIcon.Info);
        }

        bool TryRegister(Keys key, uint mods)
        {
            Native.UnregisterHotKey(hotkey.Handle, 1);
            hotkeyRegistered = Native.RegisterHotKey(hotkey.Handle, 1, mods | Native.MOD_NOREPEAT, (uint)key);
            return hotkeyRegistered;
        }

        void UpdateLabels()
        {
            var text = hotkeyRegistered ? $"Geçiş: {cfg.Label}" : "Kısayol atanmadı";
            labelItem.Text = text;
            tray.Text = ("DualCursor — " + text).Length > 63 ? "DualCursor" : "DualCursor — " + text;
        }

        void OpenSettings(string message = null)
        {
            if (settings != null && !settings.IsDisposed) { settings.Activate(); return; }

            // Pencere açıkken eski kısayol tetiklenmesin
            Native.UnregisterHotKey(hotkey.Handle, 1);
            hotkeyRegistered = false;

            settings = new SettingsForm(cfg, TryRegister, message);
            settings.FormClosed += (_, __) =>
            {
                if (!settings.Saved)   // iptal: eski kısayolu geri yükle
                    TryRegister(cfg.ParseKey(), cfg.ParseModifiers());
                UpdateLabels();
                if (settings.Saved)
                    tray.ShowBalloonTip(2000, "DualCursor", $"Yeni kısayol: {cfg.Label}", ToolTipIcon.Info);
                settings = null;
            };
            settings.Show();
            settings.Activate();
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
