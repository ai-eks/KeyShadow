using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Windows.Forms;

namespace keyshadow
{
    internal static class Program
    {
        internal static readonly int ShowMessage = Native.RegisterWindowMessage("keyshadow.Show.v1");

        [STAThread]
        private static void Main(string[] args)
        {
            bool first;
            using (var mutex = new Mutex(true, "Local\\keyshadow.v1", out first))
            {
                if (!first)
                {
                    if (Array.IndexOf(args, "--startup") < 0)
                        Native.PostMessage(new IntPtr(0xffff), ShowMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new KeyboardForm());
            }
        }
    }

    internal static class Native
    {
        internal const int ExTransparent = 0x20, ExNoActivate = 0x08000000, ExToolWindow = 0x80;
        [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }

    internal sealed class KeyInfo
    {
        internal readonly string Letter, Initial, Final;
        internal readonly RectangleF Bounds;
        internal int VirtualKey { get { return Letter == ";" ? 0xBA : Letter[0]; } }
        internal KeyInfo(string letter, string initial, string final, float x, float y)
        {
            Letter = letter; Initial = initial; Final = final;
            Bounds = new RectangleF(x, y, 70, 58);
        }
    }

    internal sealed class Preferences
    {
        internal int X = int.MinValue, Y = int.MinValue, Opacity = 85;
        internal float Size = 1;
        internal bool FollowInput = true;
        internal int AutoHideSeconds = 2;
        internal string ThemeId = "navy";
        internal bool Compact;
        internal string SchemeId = "flypy";
        internal bool Hints = true;
        // Placement (FollowInput) and visibility (AlwaysShow) are independent settings.
        internal bool AlwaysShow;
        internal static readonly string PathName = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "keyshadow", "settings.txt");
        internal static Preferences Load(string path)
        {
            var result = new Preferences();
            try
            {
                if (!File.Exists(path)) return result;
                string[] lines = File.ReadAllLines(path);
                int x, y, opacity; float size;
                if (lines.Length >= 4 && int.TryParse(lines[0], out x) && int.TryParse(lines[1], out y)
                    && int.TryParse(lines[2], out opacity)
                    && float.TryParse(lines[3], NumberStyles.Float, CultureInfo.InvariantCulture, out size)
                    && !float.IsNaN(size) && !float.IsInfinity(size))
                {
                    result.X = x; result.Y = y;
                    result.Opacity = Math.Max(35, Math.Min(95, opacity));
                    result.Size = Math.Max(.5f, Math.Min(1.5f, size));
                    bool follow;
                    if (lines.Length >= 5 && bool.TryParse(lines[4], out follow)) result.FollowInput = follow;
                    int delay;
                    if (lines.Length >= 6 && int.TryParse(lines[5], out delay) && delay >= 0 && delay <= 60)
                        result.AutoHideSeconds = delay;
                    if (lines.Length >= 7) result.ThemeId = KeyboardTheme.Find(lines[6]).Id;
                    bool compact;
                    if (lines.Length >= 8 && bool.TryParse(lines[7], out compact)) result.Compact = compact;
                    if (lines.Length >= 9) result.SchemeId = PinyinScheme.FromId(lines[8]).Id;
                    bool hints, always;
                    if (lines.Length >= 10 && bool.TryParse(lines[9], out hints)) result.Hints = hints;
                    if (lines.Length >= 11 && bool.TryParse(lines[10], out always)) result.AlwaysShow = always;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return result;
        }

        internal void Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, new[] { X.ToString(), Y.ToString(), Opacity.ToString(), Size.ToString(CultureInfo.InvariantCulture), FollowInput.ToString(), AutoHideSeconds.ToString(CultureInfo.InvariantCulture), ThemeId, Compact.ToString(), SchemeId, Hints.ToString(), AlwaysShow.ToString() });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal sealed class KeyboardForm : Form
    {
        internal readonly List<KeyInfo> Keys = new List<KeyInfo>();
        private readonly Preferences preferences;
        private readonly Func<IntPtr, bool> chineseInput;
        private readonly Func<bool> passwordInput;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu, quickMenu;
        private readonly ToolStripMenuItem passItem, visibilityItem, followInputItem, alwaysItem, startupItem;
        private readonly ToolStripMenuItem schemeMenu, themeMenu, delayMenu;
        private readonly ToolTip tooltip = new ToolTip();
        private readonly Icon appIcon;
        private readonly Bitmap appLogo;
        private float dpi;
        private float scale;
        private bool passThrough, dragging, showRules, showHotKey, passHotKey;
        private bool renderQueued;
        private KeyboardMonitor monitor;
        private MouseMonitor mouse;
        private IntPtr clickWindow;
        private Point clickPoint;
        private readonly HashSet<int> pressed = new HashSet<int>();
        private readonly Dictionary<int, DateTime> flashes = new Dictionary<int, DateTime>();
        private readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer { Interval = 40 };
        private readonly System.Windows.Forms.Timer inputRefresh = new System.Windows.Forms.Timer { Interval = 180 };
        private readonly InputLocator inputLocator = new InputLocator();
        private IntPtr privacyWindow, privacyFocus;
        private int privacyGeneration;
        private bool privacyQueryBusy;
        private bool? passwordState;
        private DateTime privacyChecked = DateTime.MinValue, privacyQueryStarted;
        private bool inputQueryBusy, manuallyHidden;
        // collapsed: the 收起 key hid the keyboard until the next Chinese input.
        // positioned: the current input already has a place, so missing geometry keeps it there.
        private bool collapsed, positioned, hintsSuppressed, lastFrameHints;
        private DateTime privacyPendingSince = DateTime.MinValue;
        private int inputGeneration, pendingInputQueries, latestInputQuery;
        private DateTime inputQueryStarted;
        private IntPtr queriedForeground;
        private DateTime lastAnchorTime = DateTime.MinValue, previewUntil = DateTime.MinValue;
        private DateTime positionUntil = DateTime.MinValue;
        private DateTime typingUntil = DateTime.MinValue;
        private IntPtr typingWindow;
        private bool following = true;
        private string pending = "", lastPair = "", syllable = "";
        private IntPtr inputWindow;
        private Point dragStart, formStart;
        private int hovered = -1;
        private int quickAction = -1;
        private KeyboardTheme theme;
        private PinyinScheme scheme;
        private PinyinGuide guide;
        // Bottom bar order, shared with macOS: scheme, theme, delay, position, display, hints, rules.
        private readonly RectangleF[] actions = {
            new RectangleF(736, 305, 60, 28), new RectangleF(440, 19, 28, 28),
            new RectangleF(528, 19, 28, 28), new RectangleF(604, 19, 56, 28),
            new RectangleF(672, 19, 56, 28), new RectangleF(740, 19, 56, 28),
            new RectangleF(648, 305, 80, 28), new RectangleF(224, 19, 28, 28),
            new RectangleF(312, 19, 28, 28), new RectangleF(436, 305, 92, 28),
            new RectangleF(24, 305, 150, 28), new RectangleF(182, 305, 126, 28),
            new RectangleF(63, 194, 70, 58), new RectangleF(316, 305, 112, 28),
            new RectangleF(687, 194, 70, 58), new RectangleF(536, 305, 104, 28)
        };

        internal KeyboardForm() : this(Preferences.Load(Preferences.PathName)) { }

        internal KeyboardForm(Preferences settings) : this(settings, InputMethod.IsChinese) { }

        internal KeyboardForm(Preferences settings, Func<IntPtr, bool> isChineseInput) : this(settings, isChineseInput, null) { }

        internal KeyboardForm(Preferences settings, Func<IntPtr, bool> isChineseInput, Func<bool> isPasswordInput)
        {
            preferences = settings;
            chineseInput = isChineseInput;
            passwordInput = isPasswordInput;
            theme = KeyboardTheme.Find(preferences.ThemeId);
            scheme = PinyinScheme.FromId(preferences.SchemeId);
            following = preferences.Hints;
            guide = new PinyinGuide(scheme);
            Text = "键影 · " + scheme.Name;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = theme.Background;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            using (Graphics g = CreateGraphics()) dpi = g.DpiX / 96f;
            BuildKeys();
            appIcon = AppIcon.Create();
            appLogo = AppIcon.Render(128);
            Icon = appIcon;
            menu = new ContextMenuStrip();
            menu.Font = new Font("Microsoft YaHei UI", 9);
            quickMenu = new ContextMenuStrip { Font = menu.Font };
            quickMenu.Closed += delegate { quickAction = -1; Invalidate(); };
            EventHandler dismissMenus = delegate { quickMenu.Close(); menu.Close(); };
            LocationChanged += dismissMenus;
            SizeChanged += dismissMenus;
            // Menu order is shared with the macOS menu bar.
            visibilityItem = new ToolStripMenuItem("关闭提示    Ctrl+Alt+F8", null, delegate { ToggleVisible(); });
            passItem = new ToolStripMenuItem("鼠标穿透    Ctrl+Alt+F9", null, delegate { SetPassThrough(!passThrough); });
            menu.Items.Add(visibilityItem);
            menu.Items.Add("收起键盘（下次输入自动显示）", null, delegate { HideManually(); });
            menu.Items.Add(passItem);
            menu.Items.Add(new ToolStripSeparator());
            followInputItem = new ToolStripMenuItem("输入时跟随位置", null, delegate { SetFollowInput(!preferences.FollowInput); });
            alwaysItem = new ToolStripMenuItem("一直显示键盘", null, delegate { SetAlwaysShow(!preferences.AlwaysShow); });
            startupItem = new ToolStripMenuItem("开机启动", null, delegate { ToggleStartup(); });
            schemeMenu = new ToolStripMenuItem("双拼方案");
            foreach (PinyinScheme value in PinyinScheme.All)
            {
                PinyinScheme choice = value;
                var item = new ToolStripMenuItem(choice.Name, null, delegate { ChangeScheme(choice); });
                item.Tag = choice.Id;
                schemeMenu.DropDownItems.Add(item);
            }
            schemeMenu.DropDownOpening += delegate { UpdateQuickMenuChecks(); };
            menu.Items.Add(schemeMenu);
            themeMenu = new ToolStripMenuItem("配色主题");
            foreach (KeyboardTheme value in KeyboardTheme.All)
            {
                KeyboardTheme choice = value;
                var item = new ToolStripMenuItem(choice.Name, null, delegate { ChangeTheme(choice); });
                item.Tag = choice.Id;
                themeMenu.DropDownItems.Add(item);
            }
            themeMenu.DropDownOpening += delegate { UpdateQuickMenuChecks(); };
            menu.Items.Add(themeMenu);
            delayMenu = new ToolStripMenuItem("停止输入后收起");
            foreach (int value in new[] { 1, 2, 3, 5, 10, 30, 0 })
            {
                int seconds = value;
                string label = seconds == 0 ? "不因停顿收起" : seconds + " 秒" + (seconds == 2 ? "（默认）" : "");
                var item = new ToolStripMenuItem(label, null, delegate { ChangeAutoHideDelay(seconds); });
                item.Tag = seconds;
                delayMenu.DropDownItems.Add(item);
            }
            delayMenu.DropDownOpening += delegate { UpdateQuickMenuChecks(); };
            menu.Items.Add(delayMenu);
            var sizeMenu = new ToolStripMenuItem("键盘大小");
            foreach (int value in new[] { 50, 60, 75, 90, 100, 115, 130, 150 })
            {
                int percent = value;
                sizeMenu.DropDownItems.Add(percent + "%", null, delegate { ChangeSize(percent); });
            }
            menu.Items.Add(sizeMenu);
            var opacityMenu = new ToolStripMenuItem("不透明度");
            foreach (int value in new[] { 35, 50, 65, 75, 85, 95 })
            {
                int level = value;
                opacityMenu.DropDownItems.Add(level + "%", null, delegate { ChangeOpacity(level); });
            }
            menu.Items.Add(opacityMenu);
            var compactItem = new ToolStripMenuItem("紧凑模式", null, delegate { SetCompact(!preferences.Compact); });
            menu.Items.Add(compactItem);
            menu.Items.Add(followInputItem);
            menu.Items.Add(alwaysItem);
            var guideItem = new ToolStripMenuItem("下一键韵母提示", null, delegate { ToggleHints(); });
            menu.Items.Add(guideItem);
            var rulesItem = new ToolStripMenuItem("零声母规则", null, delegate { ToggleRules(); });
            menu.Items.Add(rulesItem);
            menu.Items.Add("恢复位置与透明度", null, delegate { ResetPlacement(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            // Build.ps1 stamps the assembly version from the repository's VERSION file.
            menu.Items.Add(new ToolStripMenuItem("版本 " + typeof(KeyboardForm).Assembly.GetName().Version.ToString(3)) { Enabled = false });
            menu.Items.Add("退出", null, delegate { Close(); });
            menu.Opening += delegate {
                // The label follows the user's on/off choice, not whether the window happens to be visible.
                visibilityItem.Text = (manuallyHidden ? "恢复提示" : "关闭提示") + (showHotKey ? "    Ctrl+Alt+F8" : "    快捷键被占用");
                passItem.Text = "鼠标穿透" + (passHotKey ? "    Ctrl+Alt+F9" : "    快捷键被占用");
                passItem.Checked = passThrough;
                followInputItem.Checked = preferences.FollowInput;
                alwaysItem.Checked = preferences.AlwaysShow;
                UpdateStartupItem();
                compactItem.Checked = preferences.Compact;
                guideItem.Checked = following;
                rulesItem.Checked = showRules;
                rulesItem.Enabled = !preferences.Compact;
            };
            tray = new NotifyIcon { Icon = appIcon, Text = "键影 · " + scheme.Name + " · 双击恢复", ContextMenuStrip = menu, Visible = true };
            tray.MouseDoubleClick += delegate(object sender, MouseEventArgs e) {
                if (e.Button == MouseButtons.Left) { SetPassThrough(false); Reveal(); }
            };
            Location = preferences.X == int.MinValue ? Screen.FromPoint(Cursor.Position).WorkingArea.Location
                : new Point(preferences.X, preferences.Y);
            ApplySize();
            if (preferences.X == int.MinValue) CenterNearBottom();
            KeepOnScreen();
            tooltip.InitialDelay = 450;
            tooltip.ReshowDelay = 100;
            refresh.Tick += delegate {
                if (flashes.Count == 0) return;
                var expired = new List<int>();
                foreach (var flash in flashes) if (DateTime.UtcNow >= flash.Value && !pressed.Contains(flash.Key)) expired.Add(flash.Key);
                foreach (int key in expired) flashes.Remove(key);
                if (expired.Count != 0) Invalidate();
            };
            refresh.Start();
            inputRefresh.Tick += delegate { TrackInput(); };
            inputRefresh.Start();
        }

        private bool MenusOpen
        {
            get { return menu.Visible || quickMenu.Visible; }
        }

        private void UpdateStartupItem()
        {
            try
            {
                startupItem.Checked = StartupRegistration.IsEnabled(Application.ExecutablePath);
                startupItem.Enabled = true;
                startupItem.Text = "开机启动";
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is SecurityException)) throw;
                startupItem.Checked = false;
                startupItem.Enabled = false;
                startupItem.Text = "开机启动（无法读取）";
            }
        }

        private void ToggleStartup()
        {
            try
            {
                bool enabled = !StartupRegistration.IsEnabled(Application.ExecutablePath);
                StartupRegistration.SetEnabled(enabled, Application.ExecutablePath);
                UpdateStartupItem();
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException)
                    && !(error is SecurityException) && !(error is InvalidOperationException)) throw;
                MessageBox.Show(this, "无法更改开机启动设置：\n" + error.Message, "键影", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private bool InteractingWithKeyboard()
        {
            return dragging || MenusOpen || (Visible && !passThrough && Bounds.Contains(Cursor.Position));
        }

        private void UpdateQuickMenuChecks()
        {
            foreach (ToolStripMenuItem item in schemeMenu.DropDownItems) item.Checked = (string)item.Tag == scheme.Id;
            foreach (ToolStripMenuItem item in themeMenu.DropDownItems) item.Checked = (string)item.Tag == theme.Id;
            foreach (ToolStripMenuItem item in delayMenu.DropDownItems) item.Checked = (int)item.Tag == preferences.AutoHideSeconds;
        }

        private void ShowQuickMenu(ToolStripMenuItem item, int action)
        {
            quickMenu.Close(); menu.Close();
            UpdateQuickMenuChecks();
            tooltip.Hide(this);
            while (quickMenu.Items.Count > 0) quickMenu.Items[0].Dispose();
            // A submenu retains its parent menu's position; use an independent popup for the toolbar.
            foreach (ToolStripMenuItem option in item.DropDownItems)
            {
                ToolStripMenuItem choice = option;
                quickMenu.Items.Add(new ToolStripMenuItem(choice.Text, null, delegate { choice.PerformClick(); })
                    { Tag = choice.Tag, Checked = choice.Checked });
            }
            quickAction = action;
            RectangleF bounds = actions[action];
            quickMenu.Show(this, new Point((int)(bounds.Left * scale), (int)(bounds.Top * scale)),
                ToolStripDropDownDirection.AboveRight);
            Invalidate();
        }

        private bool TypingActive
        {
            get { return DateTime.UtcNow < typingUntil && typingWindow == KeyboardMonitor.GetForegroundWindow(); }
        }

        private void RenewTypingDeadline()
        {
            typingUntil = preferences.AutoHideSeconds == 0 ? DateTime.MaxValue
                : DateTime.UtcNow.AddSeconds(preferences.AutoHideSeconds);
        }

        internal void ChangeAutoHideDelay(int seconds)
        {
            preferences.AutoHideSeconds = seconds;
            // A settings menu can temporarily take focus away from the typing window.
            if (!manuallyHidden && DateTime.UtcNow < typingUntil) RenewTypingDeadline();
            Invalidate(); Save();
        }

        private string AutoHideHint
        {
            get { return preferences.AutoHideSeconds == 0 ? "停顿时保持显示" : "停止 " + preferences.AutoHideSeconds + " 秒收起"; }
        }

        // "Always show" keeps a static keyboard on screen; placement is a separate setting.
        private bool StaticVisible
        {
            get { return preferences.AlwaysShow && !manuallyHidden && !collapsed; }
        }

        // Ends the dynamic display: an always-shown keyboard stays as a static keyboard, otherwise it hides.
        private void Conceal()
        {
            if (!StaticVisible) { Hide(); return; }
            if (!Visible) Show();
            else if (lastFrameHints) Invalidate();
        }

        private void TrackInput()
        {
            if (manuallyHidden || MenusOpen || dragging) return;
            if (!CanDisplayInput()) return;
            if (typingWindow != KeyboardMonitor.GetForegroundWindow()) CancelTyping();
            if ((TypingActive || pending.Length != 0) && !chineseInput(typingWindow))
            {
                CancelTyping(); previewUntil = DateTime.MinValue; Conceal();
                return;
            }
            if (!TypingActive)
            {
                if (DateTime.UtcNow > previewUntil) Conceal();
                else if (!Visible) Show();
                return;
            }
            collapsed = false; // Chinese typing brings back a collapsed keyboard.
            // Fixed placement still obeys the IME state and typing timeout.
            if (!preferences.FollowInput)
            {
                if (!Visible) Show();
                return;
            }
            if (InteractingWithKeyboard()) return;
            // A caret alone never requests a move. Only a recent typing key does.
            if (positionUntil == DateTime.MinValue) return;
            if (DateTime.UtcNow > positionUntil)
            {
                // Geometry did not arrive in time: use the latest click or the saved position.
                inputGeneration++;
                PlaceInput(null);
                return;
            }
            InputAnchor native;
            if (inputLocator.TryGetNative(out native))
            {
                latestInputQuery++; inputQueryBusy = false;
                UpdateInputAnchor(native);
                return;
            }
            // A slow accessibility provider must never block the keyboard hook or the UI thread.
            // A new input waits for its place; an input that already has one keeps it.
            if (!positioned && DateTime.UtcNow > previewUntil && DateTime.UtcNow - lastAnchorTime > TimeSpan.FromMilliseconds(700))
                Conceal();
            IntPtr foreground = KeyboardMonitor.GetForegroundWindow();
            if (pendingInputQueries >= 2 || (inputQueryBusy && queriedForeground == foreground
                && DateTime.UtcNow - inputQueryStarted < TimeSpan.FromMilliseconds(1500))) return;
            inputQueryBusy = true;
            pendingInputQueries++;
            queriedForeground = foreground;
            inputQueryStarted = DateTime.UtcNow;
            DateTime started = inputQueryStarted;
            int query = ++latestInputQuery;
            int generation = inputGeneration;
            ThreadPool.QueueUserWorkItem(delegate {
                InputAnchor found;
                if (!inputLocator.TryGet(out found)) found = null;
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((Action)delegate {
                        pendingInputQueries--;
                        if (query != latestInputQuery) return;
                        inputQueryBusy = false;
                        if (generation == inputGeneration && DateTime.UtcNow - started < TimeSpan.FromMilliseconds(650))
                            PlaceInput(found);
                    });
                }
                catch (InvalidOperationException) { } // Window closed while the provider was responding.
            });
        }

        internal void UpdateInputAnchor(InputAnchor anchor)
        {
            if (anchor == null || DateTime.UtcNow > positionUntil) return; // Only a recent typing key may move it.
            PlaceInput(anchor);
        }

        // Follow the input: caret or field geometry first, then the latest click in the same window,
        // then the saved position. Once placed, missing geometry keeps the keyboard where it is.
        private void PlaceInput(InputAnchor anchor)
        {
            if (!preferences.FollowInput || manuallyHidden || !TypingActive || positionUntil == DateTime.MinValue
                || InteractingWithKeyboard()) return;
            IntPtr foreground = KeyboardMonitor.GetForegroundWindow();
            if (anchor != null && anchor.Foreground != foreground) return;
            if (!CanDisplayInput()) return;
            if (!chineseInput(foreground)) { CancelTyping(); Conceal(); return; }
            positionUntil = DateTime.MinValue;
            collapsed = false;
            if (anchor != null) { lastAnchorTime = DateTime.UtcNow; MoveNear(anchor.Bounds); }
            else if (!positioned)
            {
                if (clickWindow == foreground) MoveNear(new Rectangle(clickPoint, new Size(1, 1)));
                else RestorePosition();
            }
            positioned = true;
            if (!Visible) Show();
        }

        private void MoveNear(Rectangle bounds)
        {
            Rectangle area = Screen.FromRectangle(bounds).WorkingArea;
            Point next = InputPlacement.Place(bounds, Size, area, (int)(72 * dpi));
            if (Location != next)
            {
                Location = next;
                ApplySize();
                // Moving to another monitor may have changed DPI and therefore the keyboard size.
                Location = InputPlacement.Place(bounds, Size, area, (int)(72 * dpi));
            }
        }

        private void RestorePosition()
        {
            if (preferences.X == int.MinValue) CenterNearBottom();
            else Location = new Point(preferences.X, preferences.Y);
            ApplySize(); KeepOnScreen();
        }

        // A click in another window marks a new input location; it is kept only to estimate placement.
        internal void RecordInputClick(Point point)
        {
            IntPtr foreground = KeyboardMonitor.GetForegroundWindow();
            if (IsDisposed || MenusOpen || dragging || foreground == IntPtr.Zero || foreground == Handle
                || (Visible && !passThrough && Bounds.Contains(point))) return;
            clickWindow = foreground; clickPoint = point;
            if (!preferences.FollowInput) return;
            previewUntil = DateTime.MinValue;
            CancelTyping(); Conceal();
        }

        internal void SetFollowInput(bool enabled, bool keepPosition = false)
        {
            if (enabled == preferences.FollowInput) return;
            if (enabled) Save(); // Keep the last fixed position, not the moving caret position.
            preferences.FollowInput = enabled;
            inputGeneration++;
            lastAnchorTime = DateTime.MinValue;
            typingUntil = DateTime.MinValue;
            collapsed = false;
            if (!enabled && !keepPosition) RestorePosition();
            if (keepPosition) Reveal();
            else
            {
                previewUntil = DateTime.MinValue;
                CancelTyping(); Conceal();
            }
            Save();
        }

        internal void SetAlwaysShow(bool enabled)
        {
            preferences.AlwaysShow = enabled;
            collapsed = false;
            previewUntil = DateTime.MinValue;
            CancelTyping(); Conceal();
            Save();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { monitor = new KeyboardMonitor(OnGlobalKey); }
            catch (System.ComponentModel.Win32Exception) { following = false; Invalidate(); }
            try
            {
                mouse = new MouseMonitor(delegate(Point point) {
                    if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate { RecordInputClick(point); });
                });
            }
            catch (System.ComponentModel.Win32Exception) { } // Without clicks, placement falls back to the saved position.
            if (DateTime.UtcNow > previewUntil) BeginInvoke((Action)TrackInput);
        }

        internal void OnGlobalKey(int key, bool down)
        {
            if (IsDisposed || manuallyHidden) return;
            if (MenusOpen)
            {
                if (!down && pressed.Remove(key)) Invalidate();
                return;
            }
            bool? password = CheckInputPrivacy(down);
            if (password == true) { SuppressInput(); return; }
            if (password == null) Conceal(); // Do not render a browser key before its field is checked.
            IntPtr current = KeyboardMonitor.GetForegroundWindow();
            if (current != inputWindow) { ResetInput(); pressed.Clear(); flashes.Clear(); positioned = false; inputWindow = current; }
            bool letter = key >= 65 && key <= 90;
            bool schemeKey = letter || (key == 0xBA && scheme.UsesSemicolon);
            if (!down) { pressed.Remove(key); Invalidate(); return; }
            if (KeyboardMonitor.IsModifierKey(key) || KeyboardMonitor.IsHotKey(key)) return;
            bool modifiers = KeyboardMonitor.ModifiersDown();
            // Pinyin keys: any letter, or the Microsoft semicolon as a second key.
            if (schemeKey && !modifiers && (letter || pending.Length == 1))
            {
                typingWindow = current;
                RenewTypingDeadline();
                previewUntil = DateTime.MinValue;
                // Check IME state and update visibility/position after the hook, preserving the first key.
                RequestInputUpdate();
                if (!pressed.Add(key)) return; // Ignore OS key-repeat when forming a syllable.
                flashes[key] = DateTime.UtcNow.AddMilliseconds(160);
                string value = key == 0xBA ? ";" : ((char)(key + 32)).ToString();
                if (pending.Length == 0) { pending = value; lastPair = ""; syllable = ""; }
                else { lastPair = pending + value; syllable = guide.Decode(lastPair); pending = ""; }
            }
            else if (key == 8 && !modifiers)
            {
                // Backspace corrects the pair; it neither starts nor ends an input.
                if (!TypingActive) return;
                RenewTypingDeadline(); RequestInputUpdate();
                if (pending.Length != 0) ResetInput();
                else if (lastPair.Length == 2) { pending = lastPair.Substring(0, 1); lastPair = ""; syllable = ""; }
            }
            else EndTyping(); // Any other key (space, digits, punctuation, Enter, Esc, arrows, shortcuts) ends the input.
            Invalidate();
        }

        private void ResetInput() { pending = ""; lastPair = ""; syllable = ""; }

        private void EndTyping()
        {
            typingUntil = previewUntil = positionUntil = DateTime.MinValue;
            inputGeneration++;
            positioned = false;
            ResetInput();
            BeginInvoke((Action)delegate {
                if (!IsDisposed && !TypingActive && DateTime.UtcNow > previewUntil) Conceal();
            });
        }

        private void SuppressInput()
        {
            previewUntil = DateTime.MinValue;
            CancelTyping(); Conceal();
        }

        private bool CanDisplayInput()
        {
            bool? blocked = CheckInputPrivacy(false);
            if (blocked == false) return true;
            if (blocked == true) SuppressInput();
            else Conceal(); // A new key waits for its field check before hints are drawn.
            return false;
        }

        // true: an identified password or read-only field; false: show hints; null: a new key awaits its check.
        // Unknown, unavailable or slow metadata counts as ordinary input, as on macOS.
        private bool? CheckInputPrivacy(bool newKey)
        {
            if (passwordInput != null) return passwordInput();
            IntPtr foreground, focus;
            bool? native = InputPrivacy.ReadNative(out foreground, out focus);
            if (foreground != privacyWindow || focus != privacyFocus)
            {
                privacyWindow = foreground; privacyFocus = focus;
                privacyGeneration++; passwordState = null; privacyChecked = DateTime.MinValue;
                privacyPendingSince = DateTime.UtcNow;
                CancelTyping(); Conceal();
            }
            if (native.HasValue) { passwordState = native; return native; }
            // Virtual fields in a browser share HWNDs. A new key must not reuse a previous field's verdict.
            if (newKey)
            {
                privacyGeneration++; passwordState = null; privacyChecked = DateTime.MinValue;
                privacyPendingSince = DateTime.UtcNow;
            }
            // A check that misses its deadline leaves the field unknown, which shows hints.
            if (passwordState == null && DateTime.UtcNow - privacyPendingSince > TimeSpan.FromMilliseconds(650))
                passwordState = false;
            if (privacyQueryBusy || DateTime.UtcNow - privacyChecked < TimeSpan.FromMilliseconds(180)) return passwordState;
            privacyQueryBusy = true;
            privacyQueryStarted = DateTime.UtcNow;
            DateTime started = privacyQueryStarted;
            int generation = privacyGeneration;
            bool waitingForPrivacy = passwordState == null;
            ThreadPool.QueueUserWorkItem(delegate {
                // The hook runs before the target processes its key. Allow browser focus metadata to catch up.
                if (waitingForPrivacy) Thread.Sleep(30);
                bool stale;
                bool? result = InputPrivacy.ReadAccessible(foreground, focus, out stale);
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((Action)delegate {
                        privacyQueryBusy = false;
                        // A stale read describes another field; leave the verdict open so it is read again.
                        if (!stale && generation == privacyGeneration && InputPrivacy.SameFocus(foreground, focus)
                            && DateTime.UtcNow - started < TimeSpan.FromMilliseconds(650))
                        {
                            passwordState = result == true;
                            privacyChecked = DateTime.UtcNow;
                            if (passwordState == true) SuppressInput();
                            else if (waitingForPrivacy && TypingActive && positionUntil != DateTime.MinValue)
                                positionUntil = DateTime.UtcNow.AddMilliseconds(650);
                        }
                        TrackInput();
                    });
                }
                catch (InvalidOperationException) { } // Closed while the provider was responding.
            });
            return passwordState;
        }

        private void CancelTyping()
        {
            typingUntil = positionUntil = DateTime.MinValue;
            inputGeneration++;
            positioned = false;
            ResetInput(); pressed.Clear(); flashes.Clear();
        }

        private void RequestInputUpdate()
        {
            inputGeneration++;
            positionUntil = preferences.FollowInput ? DateTime.UtcNow.AddMilliseconds(650) : DateTime.MinValue;
            BeginInvoke((Action)TrackInput);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            if (!Visible)
            {
                // An idle pause hides the window but must not discard the first key of a syllable.
                if (manuallyHidden) ResetInput();
                // A privacy check can temporarily hide an active key; retain repeat suppression until key-up.
                if (!TypingActive) pressed.Clear();
                flashes.Clear();
            }
            base.OnVisibleChanged(e);
            if (Visible) Invalidate();
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= Native.ExNoActivate | Native.ExToolWindow | LayeredWindow.Style; return p; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            showHotKey = Native.RegisterHotKey(Handle, 1, 0x4003, (uint)System.Windows.Forms.Keys.F8);
            passHotKey = Native.RegisterHotKey(Handle, 2, 0x4003, (uint)System.Windows.Forms.Keys.F9);
            if (passThrough) Native.SetWindowLong(Handle, -20, Native.GetWindowLong(Handle, -20) | Native.ExTransparent);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Native.UnregisterHotKey(Handle, 1); Native.UnregisterHotKey(Handle, 2);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x2e0) // WM_DPICHANGED: all locator coordinates are physical screen pixels.
            {
                dpi = (message.WParam.ToInt64() & 0xffff) / 96f;
                int x = Marshal.ReadInt32(message.LParam), y = Marshal.ReadInt32(message.LParam, 4);
                Location = new Point(x, y);
                ApplySize(); KeepOnScreen();
                return;
            }
            if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            if (message.Msg == Program.ShowMessage) { SetPassThrough(false); Reveal(); return; }
            if (message.Msg == 0x312)
            {
                if (message.WParam.ToInt32() == 1) ToggleVisible();
                if (message.WParam.ToInt32() == 2) SetPassThrough(!passThrough);
                return;
            }
            if (message.Msg == 0x7e && IsHandleCreated) // Display topology changed.
                BeginInvoke((Action)delegate { ApplySize(); KeepOnScreen(); });
            base.WndProc(ref message);
        }

        private void BuildKeys()
        {
            Keys.Clear();
            string[] letters = { "QWERTYUIOP", scheme.UsesSemicolon ? "ASDFGHJKL;" : "ASDFGHJKL", "ZXCVBNM" };
            float[] starts = { 24, scheme.UsesSemicolon ? 24 : 63, 141 };
            for (int row = 0; row < letters.Length; row++)
                for (int column = 0; column < letters[row].Length; column++)
                {
                    string letter = letters[row][column].ToString();
                    char codeKey = char.ToLowerInvariant(letter[0]);
                    string initial = scheme.InitialLabel(codeKey);
                    string final = scheme.FinalLabel(codeKey);
                    Keys.Add(new KeyInfo(letter, initial, final, starts[row] + column * 78, 66 + row * 64));
                }
        }

        private int LogicalWidth { get { return preferences.Compact ? 796 : 820; } }
        private int LogicalHeight { get { return preferences.Compact ? 210 : showRules ? 442 : 344; } }

        private void ApplySize()
        {
            scale = dpi * preferences.Size;
            Rectangle area = Screen.FromPoint(Location).WorkingArea;
            scale = Math.Min(scale, Math.Min((area.Width - 24) / (float)LogicalWidth, (area.Height - 24) / (float)LogicalHeight));
            ClientSize = new Size((int)Math.Round(LogicalWidth * scale), (int)Math.Round(LogicalHeight * scale));
            Invalidate();
        }

        private void CenterNearBottom()
        {
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - 40);
        }

        private void KeepOnScreen()
        {
            Rectangle area = Screen.FromPoint(Location).WorkingArea;
            Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)),
                Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
        }

        internal void ChangeOpacity(int value)
        {
            preferences.Opacity = Math.Max(35, Math.Min(95, value));
            Invalidate(); Save();
        }

        internal double DisplayOpacity { get { return preferences.Opacity / 100.0; } }

        internal void ChangeTheme(KeyboardTheme value)
        {
            theme = value;
            preferences.ThemeId = theme.Id;
            BackColor = theme.Background;
            Invalidate(); Save();
        }

        internal void ChangeScheme(PinyinScheme value)
        {
            scheme = value;
            preferences.SchemeId = scheme.Id;
            guide = new PinyinGuide(scheme);
            BuildKeys(); ResetInput(); pressed.Clear(); flashes.Clear();
            Text = "键影 · " + scheme.Name;
            tray.Text = "键影 · " + scheme.Name + " · 双击恢复";
            Invalidate(); Save();
        }

        internal void SetCompact(bool enabled)
        {
            preferences.Compact = enabled;
            tooltip.Hide(this); tooltip.SetToolTip(this, ""); hovered = -1;
            Cursor = Cursors.Default;
            ApplySize(); KeepOnScreen(); Save();
        }

        private int SizePercent { get { return (int)Math.Round(scale / dpi * 100); } }

        internal void ChangeSize(int percent)
        {
            preferences.Size = Math.Max(50, Math.Min(150, percent)) / 100f;
            ApplySize(); KeepOnScreen(); Save();
        }

        internal void SetPassThrough(bool enabled)
        {
            passThrough = enabled;
            int style = Native.GetWindowLong(Handle, -20);
            Native.SetWindowLong(Handle, -20, enabled ? style | Native.ExTransparent : style & ~Native.ExTransparent);
            tooltip.Hide(this); hovered = -1;
            Invalidate();
        }

        private void Reveal()
        {
            manuallyHidden = false; collapsed = false; inputGeneration++;
            SetPassThrough(false);
            typingUntil = DateTime.MinValue;
            previewUntil = DateTime.UtcNow.AddSeconds(4);
            ApplySize(); KeepOnScreen(); ResetInput();
            CheckInputPrivacy(true);
            if (CanDisplayInput()) Show();
            Invalidate();
        }
        internal void HideManually()
        {
            collapsed = true;
            previewUntil = DateTime.MinValue;
            CancelTyping(); Hide();
        }
        internal void CloseKeyboard()
        {
            manuallyHidden = true;
            previewUntil = DateTime.MinValue;
            CancelTyping(); quickMenu.Close(); menu.Close(); Hide();
        }
        // Follows the user's on/off choice, not whether the window happens to be visible.
        private void ToggleVisible() { if (manuallyHidden) Reveal(); else CloseKeyboard(); }
        private void ToggleHints()
        {
            following = !following; preferences.Hints = following;
            ResetInput(); Invalidate(); Save();
        }
        // Position, size, opacity and click-through return to defaults; position and display modes stay.
        internal void ResetPlacement()
        {
            preferences.X = preferences.Y = int.MinValue;
            preferences.Opacity = 85; preferences.Size = 1;
            Location = Screen.FromPoint(Cursor.Position).WorkingArea.Location;
            ApplySize(); CenterNearBottom();
            Reveal(); Save();
        }
        private void ToggleRules() { showRules = !showRules; ApplySize(); KeepOnScreen(); }
        private void Save()
        {
            if (!preferences.FollowInput) { preferences.X = Left; preferences.Y = Top; }
            preferences.Save(Preferences.PathName);
        }

        protected override void OnInvalidated(InvalidateEventArgs e)
        {
            base.OnInvalidated(e);
            if (!IsHandleCreated || !Visible || IsDisposed || renderQueued) return;
            renderQueued = true;
            BeginInvoke((Action)delegate {
                renderQueued = false;
                if (IsDisposed || !IsHandleCreated || !Visible) return;
                bool safe = MenusOpen || CheckInputPrivacy(false) == false;
                if (!safe && !StaticVisible) { CanDisplayInput(); return; }
                hintsSuppressed = !safe;
                using (Bitmap frame = RenderFrame())
                    LayeredWindow.Present(Handle, Location, frame, (byte)Math.Round(DisplayOpacity * 255));
                hintsSuppressed = false;
            });
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        internal Bitmap RenderFrame()
        {
            var frame = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(frame)) DrawKeyboard(g);
            return frame;
        }

        private void DrawKeyboard(Graphics g)
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            // Draw the outer edge in physical pixels, without an aliased window Region.
            using (var path = Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 18 * scale))
            using (var brush = new SolidBrush(theme.Background))
            using (var pen = new Pen(theme.Border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.ScaleTransform(scale, scale);
            if (!preferences.Compact)
            {
                g.DrawImage(appLogo, new Rectangle(18, 6, 52, 52));
                DrawText(g, "大小", 10, FontStyle.Regular, theme.Muted, new RectangleF(186, 19, 28, 28));
                DrawText(g, SizePercent + "%", 11, FontStyle.Bold, theme.Ink, new RectangleF(260, 19, 44, 28), true);
                DrawText(g, "不透明度", 10, FontStyle.Regular, theme.Muted, new RectangleF(376, 19, 56, 28));
                DrawText(g, preferences.Opacity + "%", 11, FontStyle.Bold, theme.Ink, new RectangleF(476, 19, 44, 28), true);
                string[] labels = { showRules ? "收起规则" : "规则", "−", "+", passThrough ? "已穿透" : "穿透", "关闭", "退出",
                    following ? "拼音：开" : "拼音：关", "−", "+", preferences.FollowInput ? "位置：自动" : "位置：固定",
                    "方案：" + scheme.Name + " ▾", "配色：" + theme.Name + " ▾", null,
                    preferences.AutoHideSeconds == 0 ? "停顿不收起 ▾" : "收起：" + preferences.AutoHideSeconds + " 秒 ▾", null,
                    preferences.AlwaysShow ? "显示：常驻" : "显示：输入时" };
                for (int i = 0; i < labels.Length; i++)
                {
                    if (labels[i] == null) continue;
                    bool expanded = quickMenu.Visible && quickAction == i;
                    Color fill = hovered == i || expanded ? theme.ButtonHover : theme.Button;
                    FillRound(g, actions[i], 7, fill);
                    DrawText(g, labels[i], i == 1 || i == 2 || i == 7 || i == 8 ? 17 : 11, FontStyle.Regular,
                        i == 3 && passThrough ? theme.Initial : (i == 0 && showRules) || (i == 6 && following)
                        || (i == 9 && preferences.FollowInput) || (i == 15 && preferences.AlwaysShow) ? theme.Final : theme.Ink, actions[i], true);
                }
            }
            // Hints need a safe field; an always-shown keyboard shows them only while typing.
            bool hints = !hintsSuppressed && (!preferences.AlwaysShow || TypingActive);
            lastFrameHints = hints;
            string current = hints ? pending : "", pair = hints ? lastPair : "", decoded = hints ? syllable : "";
            GraphicsState keyState = g.Save();
            if (preferences.Compact) g.TranslateTransform(-12, -54);
            foreach (KeyInfo key in Keys)
            {
                bool special = key.Initial.Length != 0;
                bool active = hints && (pressed.Contains(key.VirtualKey) || flashes.ContainsKey(key.VirtualKey));
                bool next = following && current.Length == 1 && guide.CanFollow(current[0], char.ToLowerInvariant(key.Letter[0]));
                bool dim = following && current.Length == 1 && !next && !active;
                FillRound(g, key.Bounds, 9, active ? theme.ActiveKey : next ? theme.NextKey
                    : special ? theme.SpecialKey : theme.Key);
                using (GraphicsPath path = Round(key.Bounds, 9))
                using (var pen = new Pen(active || next ? theme.Final : special ? theme.SpecialBorder : theme.KeyBorder, active ? 2 : 1)) g.DrawPath(pen, path);
                DrawText(g, key.Letter, 18, FontStyle.Bold, dim ? theme.Muted : theme.Ink, new RectangleF(key.Bounds.X + 11, key.Bounds.Y + 4, 30, 27));
                if (special) DrawText(g, key.Initial, 12, FontStyle.Bold, theme.Initial, new RectangleF(key.Bounds.X + 39, key.Bounds.Y + 7, 25, 21), true);
                DrawText(g, key.Final, key.Final.Length > 8 ? 10 : 12, FontStyle.Regular, dim ? theme.Muted : theme.Final,
                    new RectangleF(key.Bounds.X + 2, key.Bounds.Y + 31, 66, 23), true);
            }
            DrawUtilityKey(g, 12);
            DrawUtilityKey(g, 14);
            g.Restore(keyState);
            if (preferences.Compact) return;
            FillRound(g, new RectangleF(24, 264, 772, 37), 8, theme.Guide);
            string prompt = !following ? "实时按键高亮 · 跟随提示已关闭" : current.Length == 1
                ? current.ToUpperInvariant() + "  →  " + guide.InitialLabel(current[0]) + "   ·   请选择亮框中的韵母键"
                : pair.Length == 2 ? pair.ToUpperInvariant() + "  →  " + (decoded.Length == 0 ? "无对应音节 · Esc 重置" : decoded + "   ·   继续输入下一音节")
                : "按下首键开始  ·  声母 → 韵母";
            if (monitor == null) prompt = "按键监听未运行 · 可继续查看静态键位";
            DrawText(g, prompt, 12, FontStyle.Regular, theme.Final, new RectangleF(36, 269, 550, 25));
            DrawText(g, "声母", 10, FontStyle.Regular, theme.Initial, new RectangleF(602, 269, 38, 25), true);
            DrawText(g, "韵母", 10, FontStyle.Regular, theme.Final, new RectangleF(644, 269, 38, 25), true);
            DrawText(g, "Esc 重置", 10, FontStyle.Regular, theme.Muted, new RectangleF(704, 269, 80, 25), true);
            if (showRules)
            {
                using (var pen = new Pen(theme.KeyBorder)) g.DrawLine(pen, 24, 343, 796, 343);
                DrawText(g, scheme.ZeroInitialHint, 11, FontStyle.Regular, theme.Ink, new RectangleF(25, 350, 770, 27));
                DrawText(g, scheme.UmlautHint, 11, FontStyle.Regular, theme.Final, new RectangleF(25, 378, 770, 27));
                DrawText(g, scheme.ExampleHint, 11, FontStyle.Regular, theme.Final, new RectangleF(25, 406, 770, 27));
            }
        }

        private void DrawUtilityKey(Graphics g, int action)
        {
            RectangleF bounds = actions[action];
            bool hover = hovered == action;
            FillRound(g, bounds, 9, hover ? theme.ButtonHover : theme.Key);
            using (GraphicsPath path = Round(bounds, 9))
            using (var pen = new Pen(hover ? theme.Final : theme.KeyBorder)) g.DrawPath(pen, path);
            Color ink = hover ? theme.Final : theme.Muted;
            float x = bounds.X + bounds.Width / 2, y = bounds.Y + 18;
            float direction = preferences.Compact ? -1 : 1;
            using (var pen = new Pen(ink, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (action == 12)
                {
                    g.DrawLine(pen, x - 7, y, x + 7, y);
                }
                else
                {
                    g.DrawLines(pen, new[] { new PointF(x - 5, y - 6), new PointF(x, y - 6 + direction * 3), new PointF(x + 5, y - 6) });
                    g.DrawLines(pen, new[] { new PointF(x - 5, y + 6), new PointF(x, y + 6 - direction * 3), new PointF(x + 5, y + 6) });
                }
            }
            DrawText(g, action == 12 ? "收起" : preferences.Compact ? "展开" : "紧凑", 12, FontStyle.Regular, ink,
                new RectangleF(bounds.X, bounds.Y + 31, bounds.Width, 23), true);
        }

        private static GraphicsPath Round(RectangleF bounds, float radius)
        {
            float d = radius * 2; var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }

        private static void FillRound(Graphics g, RectangleF bounds, float radius, Color color)
        {
            using (var path = Round(bounds, radius)) using (var brush = new SolidBrush(color)) g.FillPath(brush, path);
        }

        private static void DrawText(Graphics g, string text, float size, FontStyle style, Color color, RectangleF bounds, bool center = false)
        {
            using (var font = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = center ? StringAlignment.Center : StringAlignment.Near,
                LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text, font, brush, bounds, format);
        }

        private int HitAction(Point point)
        {
            var logical = new PointF(point.X / scale, point.Y / scale);
            if (preferences.Compact)
            {
                logical.X += 12; logical.Y += 54;
                return actions[12].Contains(logical) ? 12 : actions[14].Contains(logical) ? 14 : -1;
            }
            for (int i = 0; i < actions.Length; i++) if (actions[i].Contains(logical)) return i;
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { quickMenu.Close(); menu.Show(this, e.Location); return; }
            if (e.Button != MouseButtons.Left) return;
            int action = HitAction(e.Location);
            if (action == 0) ToggleRules();
            else if (action == 1) ChangeOpacity(preferences.Opacity - 5);
            else if (action == 2) ChangeOpacity(preferences.Opacity + 5);
            else if (action == 3) SetPassThrough(!passThrough);
            else if (action == 4) CloseKeyboard();
            else if (action == 12) HideManually();
            else if (action == 5) Close();
            else if (action == 6) ToggleHints();
            else if (action == 7) ChangeSize(SizePercent - 5);
            else if (action == 8) ChangeSize(SizePercent + 5);
            else if (action == 9) SetFollowInput(!preferences.FollowInput);
            else if (action == 10) ShowQuickMenu(schemeMenu, action);
            else if (action == 11) ShowQuickMenu(themeMenu, action);
            else if (action == 14) SetCompact(!preferences.Compact);
            else if (action == 13) ShowQuickMenu(delayMenu, action);
            else if (action == 15) SetAlwaysShow(!preferences.AlwaysShow);
            else if (preferences.Compact || e.Y < 60 * scale)
            {
                if (preferences.FollowInput) SetFollowInput(false, true);
                dragging = true; dragStart = Cursor.Position; formStart = Location; Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                Location = new Point(formStart.X + Cursor.Position.X - dragStart.X, formStart.Y + Cursor.Position.Y - dragStart.Y);
                return;
            }
            int action = HitAction(e.Location);
            Cursor = action >= 0 ? Cursors.Hand : preferences.Compact || e.Y < 60 * scale ? Cursors.SizeAll : Cursors.Default;
            if (hovered != action)
            {
                hovered = action;
                // Tooltips match the macOS keyboard; only the tray / menu bar wording differs.
                string[] tips = { "显示 / 收起当前双拼方案的规则", "降低不透明度", "提高不透明度", "点击穿过键盘；Ctrl+Alt+F9 或托盘双击恢复", "关闭提示并留在托盘，打字不再弹出；Ctrl+Alt+F8 或双击托盘恢复", "退出键影，结束程序", "下一键韵母提示；英文输入时可关闭。实体按键仍会高亮。", "缩小 5%（最小 50%）", "放大 5%（最大 150%，自动适应屏幕空间）", "自动：跟随输入框，读不到时用最近点击或保存的位置；固定：保持在保存的位置", "快捷切换双拼方案，请与输入法设置保持一致", "快捷选择配色主题", "只保留按键；点击展开恢复完整界面", "设置停止输入后的收起延时", "恢复完整界面和底部设置", "常驻：一直显示键盘；输入时：仅中文输入时显示，" + AutoHideHint + "，任何非字母键收起" };
                tips[12] = "暂时收起；下一次中文输入时自动显示";
                tips[14] = preferences.Compact ? "展开完整界面和底部设置" : "切换紧凑模式，只保留键区";
                tooltip.SetToolTip(this, action < 0 ? "" : tips[action]);
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging) { dragging = false; Capture = false; ApplySize(); KeepOnScreen(); Save(); }
        }

        protected override void OnMouseCaptureChanged(EventArgs e) { dragging = false; base.OnMouseCaptureChanged(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnFormClosed(FormClosedEventArgs e) { Save(); base.OnFormClosed(e); }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refresh.Stop(); refresh.Dispose();
                inputRefresh.Stop(); inputRefresh.Dispose(); inputGeneration++;
                if (monitor != null) monitor.Dispose();
                if (mouse != null) mouse.Dispose();
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                if (menu != null) menu.Dispose();
                if (quickMenu != null) quickMenu.Dispose();
                tooltip.Dispose();
                if (appIcon != null) appIcon.Dispose();
                if (appLogo != null) appLogo.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
