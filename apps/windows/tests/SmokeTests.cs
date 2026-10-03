using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using keyshadow;

internal static class SmokeTests
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    private static int count;
    private static void Check(bool success, string label)
    {
        if (!success) throw new Exception(label);
        Console.WriteLine("PASS " + label); count++;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        byte[] saved = File.Exists(Preferences.PathName) ? File.ReadAllBytes(Preferences.PathName) : null;
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string root = args[0];
            string prefsFile = Path.Combine(root, "preferences-test.txt");
            if (File.Exists(prefsFile)) File.Delete(prefsFile);
            Check(Preferences.Load(prefsFile).Opacity == 85, "default preferences");
            File.WriteAllLines(prefsFile, new[] { "100", "200", "999", "NaN" });
            Check(Preferences.Load(prefsFile).Size == 1, "invalid floating-point settings rejected");
            File.WriteAllLines(prefsFile, new[] { "-9000", "9000", "200", "5" });
            Preferences p = Preferences.Load(prefsFile);
            Check(p.Opacity == 95 && p.Size == 1.5f, "settings clamped to supported ranges");
            Check(p.FollowInput, "missing follow preference defaults to automatic position");
            Check(p.AutoHideSeconds == 2, "missing delay defaults to two seconds");
            File.WriteAllLines(prefsFile, new[] { "100", "200", "85", "1", "False" });
            Check(Preferences.Load(prefsFile).AutoHideSeconds == 2 && !Preferences.Load(prefsFile).FollowInput,
                "missing delay preserves fixed mode");
            foreach (string invalid in new[] { "-1", "61", "2147483648", "invalid" })
            {
                File.WriteAllLines(prefsFile, new[] { "100", "200", "85", "1", "False", invalid });
                Check(Preferences.Load(prefsFile).AutoHideSeconds == 2 && Preferences.Load(prefsFile).X == 100,
                    "invalid delay falls back without losing other settings: " + invalid);
            }
            foreach (int delay in new[] { 0, 30 })
            {
                p.AutoHideSeconds = delay; p.Save(prefsFile);
                Check(Preferences.Load(prefsFile).AutoHideSeconds == delay, "delay preference round trip: " + delay);
            }
            p.AutoHideSeconds = 2;
            p.FollowInput = false;
            p.Save(prefsFile);
            Check(Preferences.Load(prefsFile).X == -9000, "settings round trip");
            Check(!Preferences.Load(prefsFile).FollowInput, "fixed-position preference survives reload");
            File.WriteAllLines(prefsFile, new[] { "100", "200", "85", "1", "True", "5" });
            Check(Preferences.Load(prefsFile).ThemeId == "navy" && Preferences.Load(prefsFile).AutoHideSeconds == 5,
                "missing theme defaults to navy and preserves delay");
            File.AppendAllText(prefsFile, "unknown-theme" + Environment.NewLine);
            Check(Preferences.Load(prefsFile).ThemeId == "navy" && Preferences.Load(prefsFile).AutoHideSeconds == 5,
                "unknown theme falls back without losing other settings");
            foreach (KeyboardTheme theme in KeyboardTheme.All)
            {
                p.ThemeId = theme.Id; p.Save(prefsFile);
                Check(Preferences.Load(prefsFile).ThemeId == theme.Id, "theme preference round trip: " + theme.Id);
            }
            File.WriteAllLines(prefsFile, new[] { "100", "200", "85", "1", "True", "5", "cream" });
            Check(!Preferences.Load(prefsFile).Compact && Preferences.Load(prefsFile).ThemeId == "cream",
                "missing compact preference keeps full layout and selected theme");
            File.AppendAllText(prefsFile, "invalid" + Environment.NewLine);
            Check(!Preferences.Load(prefsFile).Compact && Preferences.Load(prefsFile).AutoHideSeconds == 5,
                "invalid compact preference keeps full layout without losing delay");
            p.Compact = true; p.Save(prefsFile);
            Check(Preferences.Load(prefsFile).Compact, "compact preference survives reload");
            File.WriteAllLines(prefsFile, new[] { "100", "200", "95", "0.75", "True", "30", "cream", "True" });
            p = Preferences.Load(prefsFile);
            Check(p.SchemeId == "flypy" && p.Compact && p.ThemeId == "cream" && p.AutoHideSeconds == 30 && p.Opacity == 95,
                "missing scheme defaults to Xiaohe and preserves other settings");
            File.AppendAllText(prefsFile, "unknown" + Environment.NewLine);
            Check(Preferences.Load(prefsFile).SchemeId == "flypy", "unknown scheme falls back to Xiaohe");
            File.WriteAllLines(prefsFile, new[] { "100", "200", "95", "0.75", "True", "30", "cream", "True", "abc" });
            p = Preferences.Load(prefsFile);
            Check(p.Hints && !p.AlwaysShow && p.SchemeId == "abc", "missing hint and display preferences keep their defaults");
            p.Hints = false; p.AlwaysShow = true; p.Save(prefsFile);
            Check(!Preferences.Load(prefsFile).Hints && Preferences.Load(prefsFile).AlwaysShow && Preferences.Load(prefsFile).SchemeId == "abc",
                "hint and display preferences survive reload");
            Check(InputPlacement.Place(new Rectangle(400, 700, 2, 24), new Size(600, 240), new Rectangle(0, 0, 1280, 960), 72).Y == 388,
                "input keyboard prefers above caret with candidate gap");
            Check(InputPlacement.Place(new Rectangle(400, 20, 2, 24), new Size(600, 240), new Rectangle(0, 0, 1280, 960), 72).Y == 116,
                "input keyboard uses below when top has no room");
            var leftMonitor = new Rectangle(-1920, 0, 1920, 1080);
            Check(leftMonitor.Contains(new Rectangle(InputPlacement.Place(new Rectangle(-20, 1030, 2, 24), new Size(600, 240), leftMonitor, 72), new Size(600, 240))),
                "input placement clamps to negative-coordinate monitor");
            bool chineseInput = true;
            using (var keyboard = new KeyboardForm(new Preferences { X = -9000, Y = 9000, FollowInput = false }, delegate { return chineseInput; }, delegate { return false; }))
            {
                Check(keyboard.Keys.Count == 26 && keyboard.Keys.Select(k => k.Letter).Distinct().Count() == 26, "26 unique keys");
                string expected = "Q:iu|W:ei|E:e|R:uan|T:ue/üe|Y:un|U:u|I:i|O:uo/o|P:ie|A:a|S:ong/iong|D:ai|F:en|G:eng|H:ang|J:an|K:ing/uai|L:iang/uang|Z:ou|X:ia/ua|C:ao|V:ui/ü|B:in|N:iao|M:ian";
                Check(string.Join("|", keyboard.Keys.Select(k => k.Letter + ":" + k.Final).ToArray()) == expected, "official Xiaohe final mapping");
                Check(string.Join("|", keyboard.Keys.Where(k => k.Initial != "").Select(k => k.Letter + ":" + k.Initial).ToArray()) == "U:sh|I:ch|V:zh", "all compound initials");
                Check(Screen.FromRectangle(keyboard.Bounds).WorkingArea.Contains(keyboard.Bounds), "off-screen saved position recovered");
                IntPtr foreground = GetForegroundWindow();
                keyboard.Show(); Application.DoEvents();
                Check(!keyboard.Visible, "fixed-position startup also waits for Chinese typing");
                SendMessage(keyboard.Handle, Program.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                Check(GetForegroundWindow() == foreground, "show does not steal foreground focus");
                int style = Native.GetWindowLong(keyboard.Handle, -20);
                Check((style & Native.ExNoActivate) != 0 && (style & Native.ExToolWindow) != 0 && (style & 8) != 0, "no-activate tool window stays topmost");
                Check(SendMessage(keyboard.Handle, 0x21, IntPtr.Zero, IntPtr.Zero).ToInt32() == 3, "mouse activation rejected without swallowing click");
                Check((style & 0x80000) != 0 && Math.Abs(keyboard.DisplayOpacity - .85) < .01, "layered 85-percent window");
                keyboard.SetPassThrough(true);
                Check((Native.GetWindowLong(keyboard.Handle, -20) & Native.ExTransparent) != 0, "click-through style enabled");
                keyboard.SetPassThrough(false);
                Check((Native.GetWindowLong(keyboard.Handle, -20) & Native.ExTransparent) == 0, "click-through style disabled");
                keyboard.ChangeOpacity(100);
                Check(Math.Abs(keyboard.DisplayOpacity - .95) < .01 && (Native.GetWindowLong(keyboard.Handle, -20) & 0x80000) != 0, "maximum opacity preserves layered window");
                keyboard.ChangeOpacity(0);
                Check(Math.Abs(keyboard.DisplayOpacity - .35) < .01, "minimum opacity retains visibility");
                keyboard.ChangeOpacity(85);
                SendMessage(keyboard.Handle, 0x312, new IntPtr(1), IntPtr.Zero);
                Check(!keyboard.Visible, "visibility hotkey dispatch hides window");
                SendMessage(keyboard.Handle, 0x312, new IntPtr(1), IntPtr.Zero);
                Check(keyboard.Visible && GetForegroundWindow() == foreground, "visibility hotkey dispatch restores without focus steal");
                SendMessage(keyboard.Handle, 0x312, new IntPtr(2), IntPtr.Zero);
                Check((Native.GetWindowLong(keyboard.Handle, -20) & Native.ExTransparent) != 0, "pass-through hotkey dispatch");
                SendMessage(keyboard.Handle, Program.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                Check(keyboard.Visible && (Native.GetWindowLong(keyboard.Handle, -20) & Native.ExTransparent) == 0, "second-launch recovery clears click-through");
                foreach (string field in new[] { "showHotKey", "passHotKey" })
                    Check((bool)typeof(KeyboardForm).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard), field + " registered successfully");
                Check(typeof(KeyboardForm).GetField("monitor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard) != null, "global keyboard hook installed");
                keyboard.OnGlobalKey(85, true);
                keyboard.OnGlobalKey(85, true);
                Check(Field(keyboard, "pending") == "u", "key repeat does not consume next syllable key");
                keyboard.OnGlobalKey(85, false);
                Check(new PinyinGuide(PinyinScheme.FromId("flypy")).CanFollow('u', 'l') && !new PinyinGuide(PinyinScheme.FromId("flypy")).CanFollow('u', 'p'), "next-key guidance filters invalid sh finals");
                keyboard.OnGlobalKey(76, true); keyboard.OnGlobalKey(76, false);
                Check(Field(keyboard, "syllable") == "shuang", "U L decodes to shuang");
                keyboard.OnGlobalKey(8, true); keyboard.OnGlobalKey(8, false);
                Check(Field(keyboard, "pending") == "u", "backspace restores first key");
                keyboard.OnGlobalKey(75, true); keyboard.OnGlobalKey(75, false);
                Check(Field(keyboard, "syllable") == "shuai", "corrected U K decodes to shuai");
                keyboard.OnGlobalKey(32, true); keyboard.OnGlobalKey(32, false);
                Check(Field(keyboard, "pending") == "" && Field(keyboard, "lastPair") == "", "space resets guide");
                keyboard.OnGlobalKey(65, true); keyboard.OnGlobalKey(65, false);
                keyboard.OnGlobalKey(65, true); keyboard.OnGlobalKey(65, false);
                Check(Field(keyboard, "syllable") == "a", "zero-initial aa guide");
                keyboard.OnGlobalKey(27, true); keyboard.OnGlobalKey(27, false);
                Check(Field(keyboard, "lastPair") == "", "escape resets guide");
                keyboard.Hide(); keyboard.Show();
                int originalWidth = keyboard.Width;
                keyboard.ChangeSize(50);
                Check(Math.Abs(keyboard.Width * 2 - originalWidth) <= 1, "50-percent scale halves window width");
                Check(Math.Abs(Preferences.Load(Preferences.PathName).Size - .5f) < .001, "50-percent size survives settings reload");
                ClickSizeButton(keyboard, 326);
                Check(Math.Abs(Preferences.Load(Preferences.PathName).Size - .55f) < .001, "visible plus button enlarges by five percent at minimum scale");
                ClickSizeButton(keyboard, 238);
                Check(Math.Abs(Preferences.Load(Preferences.PathName).Size - .5f) < .001, "visible minus button shrinks by five percent");
                ClickSizeButton(keyboard, 238);
                Check(Math.Abs(Preferences.Load(Preferences.PathName).Size - .5f) < .001, "minimum scale cannot be exceeded");
                Check(!(bool)typeof(KeyboardForm).GetField("dragging", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard), "size buttons do not start title-bar dragging");
                Rectangle workArea = Screen.FromRectangle(keyboard.Bounds).WorkingArea;
                keyboard.Location = new Point(workArea.Right - keyboard.Width, workArea.Bottom - keyboard.Height);
                keyboard.ChangeSize(150);
                Check(workArea.Contains(keyboard.Bounds), "enlarging at screen edge keeps full window visible");
                Check(GetForegroundWindow() == foreground, "resizing does not steal input focus");
                keyboard.ChangeSize(100);
                keyboard.SetPassThrough(true); // Let the state tests ignore pointer hover over the test overlay.
                Point fixedPosition = keyboard.Location;
                keyboard.SetFollowInput(true);
                Check(!keyboard.Visible, "enabling automatic mode waits for typing without a preview");
                var anchor = new InputAnchor(GetForegroundWindow(), new Rectangle(workArea.Left + 250, workArea.Top + 100, 2, 25), "TestCaret", true);
                keyboard.Hide();
                SetField(keyboard, "previewUntil", DateTime.MinValue);
                keyboard.UpdateInputAnchor(anchor);
                Check(!keyboard.Visible, "input focus alone cannot show the keyboard");
                var menu = (ContextMenuStrip)typeof(KeyboardForm).GetField("menu", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard);
                var delayMenu = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "停止输入后收起");
                delayMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(item => (int)item.Tag == 5).PerformClick();
                Check(Preferences.Load(Preferences.PathName).AutoHideSeconds == 5 && Deadline(keyboard) == DateTime.MinValue && !keyboard.Visible,
                    "delay menu saves choice without starting an idle session");
                typeof(ToolStripDropDownItem).GetMethod("OnDropDownShow", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(delayMenu, new object[] { EventArgs.Empty });
                Check(delayMenu.DropDownItems.OfType<ToolStripMenuItem>().Where(item => item.Checked).Select(item => (int)item.Tag).SequenceEqual(new[] { 5 }),
                    "delay submenu marks only the selected value");
                keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                Check((Deadline(keyboard) - DateTime.UtcNow).TotalSeconds > 4 && (Deadline(keyboard) - DateTime.UtcNow).TotalSeconds <= 5,
                    "typing uses configured five-second deadline");
                keyboard.UpdateInputAnchor(anchor);
                Check(keyboard.Visible && workArea.Contains(keyboard.Bounds), "typing automatically shows a fully visible keyboard");
                Check(Field(keyboard, "pending") == "u", "first typed key survives automatic show");
                Point typedPosition = keyboard.Location;
                var movedAnchor = new InputAnchor(anchor.Foreground, new Rectangle(workArea.Left + 650, workArea.Top + 350, 2, 25), "MovedCaret", true);
                keyboard.UpdateInputAnchor(movedAnchor);
                Check(keyboard.Location == typedPosition, "moving a caret without typing cannot move an active overlay");
                keyboard.ChangeAutoHideDelay(0);
                Check(Deadline(keyboard) == DateTime.MaxValue && Field(keyboard, "pending") == "u", "active session can disable idle timeout without losing first key");
                keyboard.ChangeAutoHideDelay(3);
                Check((Deadline(keyboard) - DateTime.UtcNow).TotalSeconds > 2 && (Deadline(keyboard) - DateTime.UtcNow).TotalSeconds <= 3,
                    "changing unlimited delay back to finite restarts countdown");
                keyboard.ChangeAutoHideDelay(2);
                var themeMenu = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "配色主题");
                DateTime beforeThemeDeadline = Deadline(keyboard);
                Rectangle beforeThemeBounds = keyboard.Bounds;
                double beforeThemeOpacity = keyboard.DisplayOpacity;
                using (var comparison = new Bitmap(keyboard.Width * 2, (keyboard.Height + 38) * 2))
                using (Graphics g = Graphics.FromImage(comparison))
                using (var titleFont = new Font("Microsoft YaHei UI", 15))
                {
                    g.Clear(Color.White);
                    for (int i = 0; i < KeyboardTheme.All.Length; i++)
                    {
                        KeyboardTheme theme = KeyboardTheme.All[i];
                        themeMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(item => (string)item.Tag == theme.Id).PerformClick();
                        Check(keyboard.BackColor == theme.Background && Preferences.Load(Preferences.PathName).ThemeId == theme.Id,
                            "theme menu applies and saves: " + theme.Id);
                        typeof(ToolStripDropDownItem).GetMethod("OnDropDownShow", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(themeMenu, new object[] { EventArgs.Empty });
                        Check(themeMenu.DropDownItems.OfType<ToolStripMenuItem>().Where(item => item.Checked).Select(item => (string)item.Tag).SequenceEqual(new[] { theme.Id }),
                            "theme submenu marks current choice: " + theme.Id);
                        Check(Deadline(keyboard) == beforeThemeDeadline && Field(keyboard, "pending") == "u" && keyboard.Bounds == beforeThemeBounds
                            && keyboard.DisplayOpacity == beforeThemeOpacity && GetForegroundWindow() == foreground && keyboard.Visible,
                            "theme switch preserves typing session, geometry, opacity and focus: " + theme.Id);
                        using (var bitmap = keyboard.RenderFrame())
                        {
                            bitmap.Save(Path.Combine(root, "theme-" + theme.Id + ".png"), ImageFormat.Png);
                            int x = (i % 2) * keyboard.Width, y = (i / 2) * (keyboard.Height + 38);
                            g.DrawString(theme.Name, titleFont, Brushes.Black, x + 16, y + 4);
                            g.DrawImageUnscaled(bitmap, x, y + 38);
                        }
                    }
                    comparison.Save(Path.Combine(root, "themes-preview.png"), ImageFormat.Png);
                }
                keyboard.ChangeTheme(KeyboardTheme.Find("navy"));
                var compactItem = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "紧凑模式");
                Size fullSize = keyboard.Size;
                DateTime beforeCompactDeadline = Deadline(keyboard);
                compactItem.PerformClick();
                Check(keyboard.Width < fullSize.Width && keyboard.Height < fullSize.Height * .65 && Preferences.Load(Preferences.PathName).Compact,
                    "compact menu shrinks window to key grid and saves preference");
                Check(Deadline(keyboard) == beforeCompactDeadline && Field(keyboard, "pending") == "u" && keyboard.DisplayOpacity == beforeThemeOpacity
                    && GetForegroundWindow() == foreground && keyboard.Visible,
                    "compact switch preserves active syllable, timeout, opacity and focus");
                typeof(ContextMenuStrip).GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu,
                    new object[] { new System.ComponentModel.CancelEventArgs() });
                Check(compactItem.Checked && !menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "零声母规则").Enabled,
                    "compact menu shows selected mode and disables hidden rules");
                var versionItem = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text.StartsWith("版本 "));
                Check(!versionItem.Enabled && versionItem.Text == "版本 " + typeof(KeyboardForm).Assembly.GetName().Version.ToString(3)
                    && menu.Items.IndexOf(versionItem) == menu.Items.Count - 2, "menu shows the app version just above exit");
                var startupItem = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "开机启动");
                var tray = (NotifyIcon)typeof(KeyboardForm).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard);
                Check(tray.ContextMenuStrip == menu && startupItem.Enabled
                    && startupItem.Checked == StartupRegistration.IsEnabled(Application.ExecutablePath),
                    "tray and keyboard share a startup toggle reflecting the registered executable");
                float compactScale = (float)typeof(KeyboardForm).GetField("scale", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard);
                Check(keyboard.Keys.All(key => key.Bounds.Left >= 12 && key.Bounds.Top >= 54
                    && (key.Bounds.Right - 12) * compactScale < keyboard.Width && (key.Bounds.Bottom - 54) * compactScale < keyboard.Height),
                    "all compact keys fit inside the window");
                RectangleF[] oldActions = (RectangleF[])typeof(KeyboardForm).GetField("actions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard);
                Check(oldActions.Where((action, index) => index != 12 && index != 14).All(action => (int)typeof(KeyboardForm).GetMethod("HitAction", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard,
                    new object[] { new Point((int)((action.X + action.Width / 2) * compactScale), (int)((action.Y + action.Height / 2) * compactScale)) }) == -1),
                    "compact mode disables every hidden toolbar hit area");
                foreach (int action in new[] { 12, 14 })
                {
                    RectangleF button = oldActions[action];
                    RectangleF neighbor = keyboard.Keys.Single(key => key.Letter == (action == 12 ? "Z" : "M")).Bounds;
                    Check(button.Size == neighbor.Size && button.Y == neighbor.Y
                        && (action == 12 ? neighbor.Left - button.Right : button.Left - neighbor.Right) == 8,
                        "utility key matches adjacent letter size and row spacing: " + action);
                    Check(keyboard.Keys.All(key => !button.IntersectsWith(key.Bounds))
                        && (button.Right - 12) * compactScale < keyboard.Width && (button.Bottom - 54) * compactScale < keyboard.Height,
                        "utility key fits compact window without overlapping letters: " + action);
                }
                SetField(keyboard, "positionUntil", DateTime.UtcNow.AddMilliseconds(650));
                keyboard.UpdateInputAnchor(anchor);
                Check(keyboard.Location == InputPlacement.Place(anchor.Bounds, keyboard.Size, workArea,
                    (int)(72 * (float)typeof(KeyboardForm).GetField("dpi", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard))),
                    "automatic positioning uses compact window dimensions");
                using (var bitmap = keyboard.RenderFrame())
                {
                    bitmap.Save(Path.Combine(root, "compact-preview.png"), ImageFormat.Png);
                }
                ClickAction(keyboard, 14);
                Check(keyboard.Size == fullSize && !Preferences.Load(Preferences.PathName).Compact,
                    "compact expand button restores full dimensions and saves choice");
                Check(Deadline(keyboard) == beforeCompactDeadline && Field(keyboard, "pending") == "u"
                    && Preferences.Load(Preferences.PathName).FollowInput
                    && !(bool)typeof(KeyboardForm).GetField("dragging", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard),
                    "expanding preserves input, timer and automatic following without starting a drag");
                var settings = Preferences.Load(Preferences.PathName);
                Check(settings.FollowInput && settings.X == fixedPosition.X && settings.Y == fixedPosition.Y, "automatic movement preserves saved fixed position");
                Point currentPosition = keyboard.Location;
                keyboard.UpdateInputAnchor(new InputAnchor(new IntPtr(-123), new Rectangle(0, 0, 2, 24), "Stale", true));
                Check(keyboard.Location == currentPosition, "stale result from another foreground window ignored");
                keyboard.HideManually();
                keyboard.SetCompact(true);
                Check(!keyboard.Visible && Deadline(keyboard) == DateTime.MinValue, "compact switch respects manual hiding");
                keyboard.SetCompact(false);
                keyboard.ChangeTheme(KeyboardTheme.Find("cream"));
                Check(!keyboard.Visible && Deadline(keyboard) == DateTime.MinValue, "theme switch respects manual hiding");
                keyboard.ChangeTheme(KeyboardTheme.Find("navy"));
                keyboard.ChangeAutoHideDelay(30);
                Check(Deadline(keyboard) == DateTime.MinValue && !keyboard.Visible, "changing delay respects manual hiding");
                keyboard.ChangeAutoHideDelay(2);
                keyboard.UpdateInputAnchor(anchor);
                Check(!keyboard.Visible, "a late caret result cannot reopen without new typing");
                SendMessage(keyboard.Handle, Program.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                keyboard.SetPassThrough(true);
                keyboard.UpdateInputAnchor(anchor);
                Check(keyboard.Visible, "tray or second-launch recovery resumes input following");
                keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                SetField(keyboard, "inputQueryBusy", true);
                SetField(keyboard, "pendingInputQueries", 2);
                SetField(keyboard, "previewUntil", DateTime.MinValue);
                SetField(keyboard, "lastAnchorTime", DateTime.UtcNow.AddSeconds(-2));
                typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                Check(!keyboard.Visible, "a new input waits for its position before showing");
                keyboard.UpdateInputAnchor(anchor);
                Check(keyboard.Visible, "fresh caret automatically restores overlay");
                float dpiScale = (float)typeof(KeyboardForm).GetField("dpi", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard);
                Point click = new Point(workArea.Left + 420, workArea.Top + 520);
                keyboard.RecordInputClick(click);
                Check(!keyboard.Visible && Field(keyboard, "pending") == "" && Deadline(keyboard) == DateTime.MinValue,
                    "a click in automatic mode ends the input so the next key is placed again");
                keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                SetField(keyboard, "positionUntil", DateTime.UtcNow.AddMilliseconds(-1));
                typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                Check(keyboard.Visible && keyboard.Location == InputPlacement.Place(new Rectangle(click, new Size(1, 1)), keyboard.Size,
                    Screen.FromPoint(click).WorkingArea, (int)(72 * dpiScale)), "missing geometry places the keyboard near the latest click");
                keyboard.RecordInputClick(click);
                SetField(keyboard, "clickWindow", new IntPtr(-123)); // The latest click belongs to another window.
                keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                SetField(keyboard, "positionUntil", DateTime.UtcNow.AddMilliseconds(-1));
                typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                Check(keyboard.Visible && keyboard.Location == fixedPosition, "without a click in this window the saved position is used");
                keyboard.OnGlobalKey(27, true); keyboard.OnGlobalKey(27, false);
                foreach (int key in new[] { 0x31, 0xBC, 0x09, 0x25, 0x70 })
                {
                    keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                    keyboard.UpdateInputAnchor(anchor);
                    Check(keyboard.Visible && Field(keyboard, "pending") == "u", "typing shows before non-letter key " + key);
                    keyboard.OnGlobalKey(key, true); keyboard.OnGlobalKey(key, false);
                    Application.DoEvents();
                    Check(!keyboard.Visible && Field(keyboard, "pending") == "" && Deadline(keyboard) == DateTime.MinValue,
                        "any non-letter key ends the input: " + key);
                }
                keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                keyboard.UpdateInputAnchor(anchor);
                keyboard.OnGlobalKey(0xA0, true); keyboard.OnGlobalKey(0xA0, false);
                Check(keyboard.Visible && Field(keyboard, "pending") == "u", "a modifier key alone does not end the input");
                keyboard.OnGlobalKey(27, true); keyboard.OnGlobalKey(27, false);
                SetField(keyboard, "inputQueryBusy", true);
                SetField(keyboard, "pendingInputQueries", 2);
                keyboard.ChangeAutoHideDelay(0);
                keyboard.OnGlobalKey(32, true); keyboard.OnGlobalKey(32, false);
                keyboard.Hide();
                keyboard.UpdateInputAnchor(anchor);
                Check(!keyboard.Visible, "commit cancels typing and rejects late positioning results");
                Check(Deadline(keyboard) == DateTime.MinValue, "commit also ends unlimited idle session");
                keyboard.ChangeAutoHideDelay(2);
                keyboard.OnGlobalKey(8, true); keyboard.OnGlobalKey(8, false);
                keyboard.UpdateInputAnchor(anchor);
                Check(!keyboard.Visible, "backspace alone cannot start an idle typing session");
                keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                keyboard.UpdateInputAnchor(anchor);
                SetField(keyboard, "typingUntil", DateTime.UtcNow.AddSeconds(-1));
                typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                keyboard.UpdateInputAnchor(anchor);
                Check(!keyboard.Visible, "typing timeout hides even when a valid caret remains focused");
                Check(Field(keyboard, "pending") == "u", "idle hiding preserves an unfinished syllable");
                keyboard.OnGlobalKey(76, true); keyboard.OnGlobalKey(76, false);
                keyboard.UpdateInputAnchor(anchor);
                Check(keyboard.Visible && Field(keyboard, "syllable") == "shuang", "second key after an idle pause continues the same syllable");
                keyboard.ChangeAutoHideDelay(5);
                SetField(keyboard, "typingUntil", DateTime.UtcNow.AddSeconds(1));
                keyboard.OnGlobalKey(8, true); keyboard.OnGlobalKey(8, false);
                Check((Deadline(keyboard) - DateTime.UtcNow).TotalSeconds > 4 && (Deadline(keyboard) - DateTime.UtcNow).TotalSeconds <= 5,
                    "active backspace renews configured delay");
                keyboard.ChangeAutoHideDelay(2);
                chineseInput = false;
                keyboard.OnGlobalKey(65, true); keyboard.OnGlobalKey(65, false);
                keyboard.UpdateInputAnchor(anchor);
                Check(!keyboard.Visible && Deadline(keyboard) == DateTime.MinValue && Field(keyboard, "pending") == "",
                    "English IME state blocks showing, positioning and partial pinyin");
                Point beforeChinese = keyboard.Location;
                chineseInput = true;
                keyboard.UpdateInputAnchor(movedAnchor);
                Check(!keyboard.Visible && keyboard.Location == beforeChinese, "switching to Chinese alone does not show or position");
                keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                keyboard.UpdateInputAnchor(movedAnchor);
                Check(keyboard.Visible && keyboard.Location != beforeChinese, "first Chinese input moves to the new caret position");
                chineseInput = false;
                typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                Check(!keyboard.Visible && Field(keyboard, "pending") == "", "switching to English hides an active overlay and clears pinyin");
                chineseInput = true;
                keyboard.UpdateInputAnchor(anchor);
                Check(!keyboard.Visible, "a late caret result cannot reopen after an IME switch");
                keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                keyboard.UpdateInputAnchor(anchor);
                SetField(keyboard, "typingUntil", DateTime.UtcNow.AddSeconds(-1));
                chineseInput = false;
                typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                Check(Field(keyboard, "pending") == "", "switching IME after an idle timeout discards the old partial syllable");
                chineseInput = true;
                SetField(keyboard, "inputQueryBusy", false);
                SetField(keyboard, "pendingInputQueries", 0);
                keyboard.SetFollowInput(false);
                Check(keyboard.Location == fixedPosition && !Preferences.Load(Preferences.PathName).FollowInput, "fixed mode restores manual location");
                Check(!keyboard.Visible, "switching to fixed position waits for new input");
                keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                Check(keyboard.Visible && keyboard.Location == fixedPosition && Field(keyboard, "pending") == "u",
                    "fixed Chinese input shows at the saved position without losing its first key");
                SetField(keyboard, "typingUntil", DateTime.UtcNow.AddSeconds(-1));
                typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                Check(!keyboard.Visible && Field(keyboard, "pending") == "u", "fixed timeout hides but preserves an unfinished syllable");
                keyboard.OnGlobalKey(76, true); keyboard.OnGlobalKey(76, false);
                typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                Check(keyboard.Visible && keyboard.Location == fixedPosition && Field(keyboard, "syllable") == "shuang",
                    "fixed typing resumes after a pause without moving");
                keyboard.ChangeAutoHideDelay(0);
                chineseInput = false;
                typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                Check(!keyboard.Visible && Deadline(keyboard) == DateTime.MinValue,
                    "English hides a fixed keyboard even with unlimited delay");
                chineseInput = true;
                typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                Check(!keyboard.Visible, "return to Chinese does not reopen fixed keyboard without typing");
                keyboard.ChangeAutoHideDelay(2);
                Check(GetForegroundWindow() == foreground, "auto show and positioning preserve foreground focus");
                keyboard.SetPassThrough(false);
                keyboard.SetFollowInput(true);
                keyboard.OnGlobalKey(85, true);
                using (var bitmap = keyboard.RenderFrame())
                {
                    bitmap.Save(Path.Combine(root, "keyboard-preview.png"), ImageFormat.Png);
                }
                keyboard.OnGlobalKey(85, false);
                int before = keyboard.Height;
                typeof(KeyboardForm).GetMethod("ToggleRules", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                Check(keyboard.Height > before, "zero-initial rules expand window");
                using (var bitmap = keyboard.RenderFrame())
                {
                    bitmap.Save(Path.Combine(root, "keyboard-rules.png"), ImageFormat.Png);
                }
                using (var stream = File.Create(Path.Combine(root, "app.ico"))) keyboard.Icon.Save(stream);
                Size rulesSize = keyboard.Size;
                keyboard.SetCompact(true);
                Check(keyboard.Height < rulesSize.Height * .5, "compact layout hides an expanded rules panel");
                keyboard.SetCompact(false);
                Check(keyboard.Size == rulesSize, "full layout restores previously expanded rules");
                keyboard.SetCompact(true);
                foreach (int size in new[] { 50, 85, 150 })
                {
                    keyboard.ChangeSize(size);
                    Check(Screen.FromRectangle(keyboard.Bounds).WorkingArea.Contains(keyboard.Bounds), "compact resizing stays within screen at " + size + " percent");
                    ClickAction(keyboard, 14);
                    Check(!Preferences.Load(Preferences.PathName).Compact && Preferences.Load(Preferences.PathName).FollowInput
                        && !(bool)typeof(KeyboardForm).GetField("dragging", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard),
                        "right mode key expands without dragging or disabling following at " + size + " percent");
                    ClickAction(keyboard, 14);
                    Check(Preferences.Load(Preferences.PathName).Compact && Preferences.Load(Preferences.PathName).FollowInput
                        && !(bool)typeof(KeyboardForm).GetField("dragging", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard),
                        "right mode key collapses without dragging or disabling following at " + size + " percent");
                }
                keyboard.ChangeSize(100);
                float dragScale = (float)typeof(KeyboardForm).GetField("scale", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard);
                var compactClick = new MouseEventArgs(MouseButtons.Left, 1, (int)(770 * dragScale), (int)(33 * dragScale), 0);
                typeof(KeyboardForm).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, new object[] { compactClick });
                Check(!keyboard.IsDisposed && keyboard.Visible && (bool)typeof(KeyboardForm).GetField("dragging", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard)
                    && !Preferences.Load(Preferences.PathName).FollowInput, "former close-button position starts compact dragging and switches to fixed mode");
                typeof(KeyboardForm).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, new object[] { compactClick });
                Check(!(bool)typeof(KeyboardForm).GetField("dragging", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard)
                    && GetForegroundWindow() == foreground, "compact drag release preserves input focus");
                var schemeMenu = menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "双拼方案");
                foreach (PinyinScheme scheme in PinyinScheme.All)
                {
                    keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                    schemeMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(item => (string)item.Tag == scheme.Id).PerformClick();
                    Check(Field(keyboard, "pending") == "" && Field(keyboard, "lastPair") == "" && Preferences.Load(Preferences.PathName).SchemeId == scheme.Id,
                        "switching scheme clears partial input and saves choice: " + scheme.Id);
                    Check(keyboard.Keys.Count == (scheme.UsesSemicolon ? 27 : 26) && keyboard.Keys.Any(key => key.VirtualKey == 0xBA) == scheme.UsesSemicolon,
                        "scheme builds the matching visible physical keys: " + scheme.Id);
                    typeof(ToolStripDropDownItem).GetMethod("OnDropDownShow", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(schemeMenu, new object[] { EventArgs.Empty });
                    Check(schemeMenu.DropDownItems.OfType<ToolStripMenuItem>().Where(item => item.Checked).Select(item => (string)item.Tag).SequenceEqual(new[] { scheme.Id }),
                        "scheme submenu marks current choice: " + scheme.Id);
                    string sequence = scheme.Id == "flypy" ? "ul" : scheme.Id == "abc" ? "vt" : "ud";
                    foreach (char key in sequence) { keyboard.OnGlobalKey(char.ToUpperInvariant(key), true); keyboard.OnGlobalKey(char.ToUpperInvariant(key), false); }
                    Check(Field(keyboard, "syllable") == "shuang", "physical key callback uses selected scheme: " + scheme.Id);
                    keyboard.SetCompact(false);
                    using (var bitmap = keyboard.RenderFrame())
                    {
                            bitmap.Save(Path.Combine(root, "scheme-" + scheme.Id + ".png"), ImageFormat.Png);
                    }
                }
                keyboard.ChangeScheme(PinyinScheme.FromId("microsoft"));
                keyboard.OnGlobalKey(76, true); keyboard.OnGlobalKey(76, false);
                keyboard.OnGlobalKey(0xBA, true);
                Check(Field(keyboard, "syllable") == "ling" && ((System.Collections.Generic.HashSet<int>)typeof(KeyboardForm).GetField("pressed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard)).Contains(0xBA),
                    "Microsoft semicolon decodes ling and receives key highlight");
                keyboard.OnGlobalKey(0xBA, false);
                keyboard.OnGlobalKey(0xBA, true); keyboard.OnGlobalKey(0xBA, false);
                Check(Field(keyboard, "pending") == "", "semicolon alone cannot become a false initial");
                keyboard.SetFollowInput(true); keyboard.SetPassThrough(true);
                menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "下一键韵母提示").PerformClick();
                Check(!Preferences.Load(Preferences.PathName).Hints, "turning off pinyin hints is saved");
                keyboard.OnGlobalKey(76, true); keyboard.OnGlobalKey(76, false);
                SetField(keyboard, "typingUntil", DateTime.UtcNow.AddMilliseconds(100));
                keyboard.OnGlobalKey(0xBA, true); keyboard.OnGlobalKey(0xBA, false);
                Check((Deadline(keyboard) - DateTime.UtcNow).TotalSeconds > 1,
                    "Microsoft semicolon renews active typing even when guidance is disabled");
                menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "下一键韵母提示").PerformClick();
                Check(Preferences.Load(Preferences.PathName).Hints, "turning pinyin hints back on is saved");
                keyboard.ChangeScheme(PinyinScheme.FromId("abc"));
                Check(keyboard.Keys.Single(key => key.Letter == "A").Initial == "zh" && keyboard.Keys.Single(key => key.Letter == "E").Initial == "ch",
                    "ABC key captions move compound initials to the correct keys");
                var quickMenu = (ContextMenuStrip)typeof(KeyboardForm).GetField("quickMenu", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard);
                foreach (int percent in new[] { 50, 85 })
                {
                    keyboard.ChangeSize(percent);
                    menu.Show(keyboard, new Point(20, 20));
                    Application.DoEvents();
                    menu.Close();
                    keyboard.Location = new Point(workArea.Left + 100, workArea.Top + 150);
                    foreach (int action in new[] { 10, 11, 13 })
                    {
                        ToolStripMenuItem popup = action == 10 ? schemeMenu : action == 11 ? themeMenu : delayMenu;
                        SendMessage(keyboard.Handle, Program.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                        ClickAction(keyboard, action);
                        Application.DoEvents();
                        CheckPopupAnchor(keyboard, quickMenu, action);
                        Check(quickMenu.Visible && quickMenu.SourceControl == keyboard && popup.Owner == menu
                            && quickMenu.Items.Count == popup.DropDownItems.Count,
                            "bottom shortcut opens an independent popup at " + percent + " percent: " + popup.Text);
                        Check(Screen.FromRectangle(quickMenu.Bounds).WorkingArea.Contains(quickMenu.Bounds),
                            "shortcut dropdown stays inside the screen: " + popup.Text);
                        SetField(keyboard, "pending", "b");
                        DateTime menuDeadline = Deadline(keyboard);
                        foreach (int key in new[] { 65, 13, 27 }) { keyboard.OnGlobalKey(key, true); keyboard.OnGlobalKey(key, false); }
                        Check(Field(keyboard, "pending") == "b" && Deadline(keyboard) == menuDeadline && keyboard.Visible,
                            "keyboard menu navigation leaves typing state unchanged: " + popup.Text);
                        SetField(keyboard, "previewUntil", DateTime.MinValue);
                        SetField(keyboard, "typingUntil", DateTime.MinValue);
                        typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                        Check(keyboard.Visible, "automatic hiding pauses while shortcut dropdown is open: " + popup.Text);
                        var selected = quickMenu.Items.OfType<ToolStripMenuItem>().First(item => item.Checked);
                        selected.PerformClick();
                        quickMenu.Close();
                    }
                }
                ClickAction(keyboard, 10);
                quickMenu.Items.OfType<ToolStripMenuItem>().Single(item => (string)item.Tag == "natural").PerformClick();
                quickMenu.Close();
                Check(Preferences.Load(Preferences.PathName).SchemeId == "natural" && keyboard.Keys.Single(key => key.Letter == "W").Final == "ia/ua",
                    "bottom scheme selection updates key captions and persists choice");
                ClickAction(keyboard, 11);
                quickMenu.Items.OfType<ToolStripMenuItem>().Single(item => (string)item.Tag == "cream").PerformClick();
                quickMenu.Close();
                Check(Preferences.Load(Preferences.PathName).ThemeId == "cream" && keyboard.BackColor == KeyboardTheme.Find("cream").Background,
                    "bottom color selection updates and saves theme");
                ClickAction(keyboard, 13);
                quickMenu.Items.OfType<ToolStripMenuItem>().Single(item => (int)item.Tag == 10).PerformClick();
                quickMenu.Close();
                Check(Preferences.Load(Preferences.PathName).AutoHideSeconds == 10, "bottom delay selection persists choice");
                keyboard.SetFollowInput(false); keyboard.SetPassThrough(false);
                foreach (int percent in new[] { 50, 85, 150 })
                {
                    keyboard.ChangeSize(percent);
                    foreach (Point position in new[] { workArea.Location,
                        new Point(workArea.Right - keyboard.Width, workArea.Bottom - keyboard.Height),
                        new Point(workArea.Left + 100, workArea.Top + 100) })
                    {
                        keyboard.Location = position;
                        foreach (int action in new[] { 10, 11, 13 })
                        {
                            ClickAction(keyboard, action); Application.DoEvents();
                            CheckPopupAnchor(keyboard, quickMenu, action);
                            quickMenu.Close();
                        }
                    }
                }
                ClickAction(keyboard, 10);
                keyboard.Left += 10;
                Check(!quickMenu.Visible, "moving the window dismisses an open shortcut popup");
                ClickAction(keyboard, 11);
                keyboard.ChangeSize(85);
                Check(!quickMenu.Visible, "resizing dismisses an open shortcut popup");
                menu.Show(keyboard, new Point(20, 20));
                schemeMenu.ShowDropDown(); Application.DoEvents();
                Check(schemeMenu.DropDown.Visible && schemeMenu.DropDownItems.OfType<ToolStripMenuItem>()
                    .Single(item => item.Checked).Tag.Equals("natural"), "right-click submenu still works and reflects shortcut selection");
                menu.Close();
                foreach (bool automatic in new[] { false, true })
                {
                    keyboard.SetFollowInput(automatic);
                    foreach (bool compact in new[] { false, true })
                    {
                        keyboard.SetCompact(compact);
                        foreach (int percent in new[] { 50, 85, 150 })
                        {
                            string mode = automatic + "/" + compact + "/" + percent;
                            keyboard.ChangeSize(percent);
                            SendMessage(keyboard.Handle, Program.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                            Size beforeHide = keyboard.Size;
                            Point beforeHidePosition = keyboard.Location;
                            ClickAction(keyboard, 12);
                            Check(!keyboard.Visible && !keyboard.IsDisposed && Deadline(keyboard) == DateTime.MinValue
                                && Preferences.Load(Preferences.PathName).Compact == compact
                                && Preferences.Load(Preferences.PathName).FollowInput == automatic
                                && !(bool)typeof(KeyboardForm).GetField("dragging", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard),
                                "left hide key hides without changing mode or starting a drag: " + mode);
                            keyboard.UpdateInputAnchor(anchor);
                            typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                            Check(!keyboard.Visible, "hide cancels stale positioning and preview: " + mode);
                            chineseInput = false;
                            keyboard.OnGlobalKey(65, true); keyboard.OnGlobalKey(65, false);
                            keyboard.UpdateInputAnchor(anchor);
                            typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                            Check(!keyboard.Visible, "English typing cannot reopen after hide: " + mode);
                            chineseInput = true;
                            typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                            Check(!keyboard.Visible, "switching to Chinese alone cannot reopen after hide: " + mode);
                            keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                            keyboard.UpdateInputAnchor(anchor);
                            typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                            Check(keyboard.Visible && Field(keyboard, "pending") == "u"
                                && (automatic || keyboard.Location == beforeHidePosition),
                                "new Chinese input reopens after collapse, preserving first key and fixed position: " + mode);
                            SendMessage(keyboard.Handle, Program.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                            Check(keyboard.Visible && keyboard.Size == beforeHide && Preferences.Load(Preferences.PathName).Compact == compact,
                                "explicit reveal restores the same layout after left hide key: " + mode);
                        }
                    }
                }
                keyboard.SetCompact(false);
                keyboard.ChangeSize(85);
                ClickAction(keyboard, 14);
                Check(Preferences.Load(Preferences.PathName).Compact && keyboard.Height < fullSize.Height * .65,
                    "right mode key switches to the compact layout");
                compactItem.PerformClick();
                Check(!Preferences.Load(Preferences.PathName).Compact, "context menu restores full layout after compact shortcut");
                foreach (int percent in new[] { 50, 85, 150 })
                {
                    keyboard.ChangeSize(percent);
                    SendMessage(keyboard.Handle, Program.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                    ClickAction(keyboard, 4);
                    Check(!keyboard.Visible && !keyboard.IsDisposed && tray.Visible,
                        "top close hides the keyboard but keeps the tray running: " + percent);
                    keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                    keyboard.UpdateInputAnchor(anchor);
                    typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                    Check(!keyboard.Visible && Field(keyboard, "pending") == "" && Deadline(keyboard) == DateTime.MinValue,
                        "top close pauses automatic input instead of merely collapsing: " + percent);
                    SendMessage(keyboard.Handle, 0x312, new IntPtr(1), IntPtr.Zero);
                    Check(keyboard.Visible, "visibility shortcut restores after top close: " + percent);
                }
                SendMessage(keyboard.Handle, 0x312, new IntPtr(1), IntPtr.Zero);
                keyboard.SetFollowInput(false); keyboard.SetFollowInput(true);
                keyboard.SetCompact(true); keyboard.ChangeSize(85);
                keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                keyboard.UpdateInputAnchor(anchor);
                Check(!keyboard.Visible, "changing placement or layout does not cancel explicit close");
                var visibilityItem = (ToolStripMenuItem)typeof(KeyboardForm).GetField("visibilityItem", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard);
                typeof(ContextMenuStrip).GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu,
                    new object[] { new System.ComponentModel.CancelEventArgs() });
                Check(visibilityItem.Text.StartsWith("恢复提示"), "closed tray menu offers restore");
                visibilityItem.PerformClick();
                Check(keyboard.Visible, "tray restore re-enables a closed compact keyboard");
                visibilityItem.PerformClick();
                keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                keyboard.UpdateInputAnchor(anchor);
                Check(!keyboard.Visible, "tray close also pauses automatic display");
                SendMessage(keyboard.Handle, Program.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                ClickAction(keyboard, 12);
                keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                keyboard.UpdateInputAnchor(anchor);
                Check(keyboard.Visible, "collapse still resumes on typing after an explicit restore");
                keyboard.SetCompact(false);
                keyboard.SetFollowInput(false); keyboard.SetPassThrough(false);
                keyboard.ChangeScheme(PinyinScheme.FromId("flypy"));
                keyboard.ChangeOpacity(95); keyboard.ChangeAutoHideDelay(2);
                if ((bool)typeof(KeyboardForm).GetField("showRules", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard))
                    typeof(KeyboardForm).GetMethod("ToggleRules", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                using (var bitmap = keyboard.RenderFrame())
                {
                    bitmap.Save(Path.Combine(root, "layout-preview.png"), ImageFormat.Png);
                }
                keyboard.SetCompact(true);
                using (var bitmap = keyboard.RenderFrame())
                {
                    bitmap.Save(Path.Combine(root, "compact-preview.png"), ImageFormat.Png);
                }
                ClickAction(keyboard, 14);
                keyboard.ChangeSize(50);
                using (var bitmap = keyboard.RenderFrame())
                {
                    bitmap.Save(Path.Combine(root, "layout-small.png"), ImageFormat.Png);
                }
                Check(GetForegroundWindow() == foreground, "shortcut menu selection preserves input focus");
                keyboard.ChangeSize(100);
                foreach (bool automatic in new[] { false, true })
                {
                    keyboard.SetFollowInput(automatic);
                    keyboard.SetPassThrough(true);
                    ClickAction(keyboard, 15);
                    Check(Preferences.Load(Preferences.PathName).AlwaysShow && keyboard.Visible && Deadline(keyboard) == DateTime.MinValue,
                        "display button keeps a static keyboard without typing: " + automatic);
                    typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                    Check(keyboard.Visible, "idle tracking keeps an always-shown keyboard: " + automatic);
                    chineseInput = false;
                    keyboard.OnGlobalKey(65, true); keyboard.OnGlobalKey(65, false);
                    keyboard.UpdateInputAnchor(anchor);
                    typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                    Check(keyboard.Visible && Field(keyboard, "pending") == "", "English typing leaves only the static keyboard: " + automatic);
                    chineseInput = true;
                    keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                    keyboard.UpdateInputAnchor(anchor);
                    typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                    Check(keyboard.Visible && Field(keyboard, "pending") == "u", "Chinese typing shows hints on the static keyboard: " + automatic);
                    keyboard.OnGlobalKey(32, true); keyboard.OnGlobalKey(32, false);
                    Application.DoEvents();
                    Check(keyboard.Visible && Field(keyboard, "pending") == "" && Deadline(keyboard) == DateTime.MinValue,
                        "a non-letter key clears hints but keeps the static keyboard: " + automatic);
                    ClickAction(keyboard, 12);
                    typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                    Check(!keyboard.Visible, "the collapse key hides an always-shown keyboard until Chinese input: " + automatic);
                    keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                    keyboard.UpdateInputAnchor(anchor);
                    typeof(KeyboardForm).GetMethod("TrackInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, null);
                    Check(keyboard.Visible && Field(keyboard, "pending") == "u", "Chinese input restores a collapsed static keyboard: " + automatic);
                    keyboard.OnGlobalKey(27, true); keyboard.OnGlobalKey(27, false);
                    Application.DoEvents();
                    ClickAction(keyboard, 4);
                    Check(!keyboard.Visible, "close hides an always-shown keyboard: " + automatic);
                    SendMessage(keyboard.Handle, 0x312, new IntPtr(1), IntPtr.Zero);
                    Check(keyboard.Visible, "the shortcut restores an always-shown keyboard: " + automatic);
                    ClickAction(keyboard, 15);
                    Check(!Preferences.Load(Preferences.PathName).AlwaysShow && !keyboard.Visible,
                        "display button returns to typing-only display: " + automatic);
                }
                typeof(ContextMenuStrip).GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu,
                    new object[] { new System.ComponentModel.CancelEventArgs() });
                Check(!keyboard.Visible && visibilityItem.Text.StartsWith("关闭提示"),
                    "an automatically hidden keyboard still offers to close its enabled hints");
                SendMessage(keyboard.Handle, 0x312, new IntPtr(1), IntPtr.Zero);
                keyboard.OnGlobalKey(85, true); keyboard.OnGlobalKey(85, false);
                keyboard.UpdateInputAnchor(anchor);
                Check(!keyboard.Visible && Field(keyboard, "pending") == "", "the shortcut closes enabled hints even while hidden");
                SendMessage(keyboard.Handle, 0x312, new IntPtr(1), IntPtr.Zero);
                Check(keyboard.Visible, "the shortcut then restores the closed hints");
                keyboard.ChangeOpacity(50); keyboard.ChangeSize(75); keyboard.SetPassThrough(true);
                keyboard.ResetPlacement();
                var reset = Preferences.Load(Preferences.PathName);
                Check(reset.FollowInput && reset.Opacity == 85 && Math.Abs(reset.Size - 1) < .001 && reset.X == int.MinValue
                    && (Native.GetWindowLong(keyboard.Handle, -20) & Native.ExTransparent) == 0 && keyboard.Visible,
                    "reset restores position, size and opacity, ends click-through and keeps automatic placement");
                keyboard.HideManually(); keyboard.ChangeScheme(PinyinScheme.FromId("natural"));
                Check(!keyboard.Visible, "scheme selection respects manually hidden state");
                SendMessage(keyboard.Handle, Program.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                ClickAction(keyboard, 5);
                Check(keyboard.IsDisposed && !tray.Visible, "top exit disposes the keyboard and removes its tray icon");
            }
            Console.WriteLine("SUCCESS " + count + " checks");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally
        {
            if (saved != null) File.WriteAllBytes(Preferences.PathName, saved);
            else if (File.Exists(Preferences.PathName)) File.Delete(Preferences.PathName);
        }
    }

    private static string Field(KeyboardForm keyboard, string name)
    {
        return (string)typeof(KeyboardForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard);
    }

    private static DateTime Deadline(KeyboardForm keyboard)
    {
        return (DateTime)typeof(KeyboardForm).GetField("typingUntil", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard);
    }

    private static void ClickSizeButton(KeyboardForm keyboard, int x)
    {
        float scale = (float)typeof(KeyboardForm).GetField("scale", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard);
        var mouse = new MouseEventArgs(MouseButtons.Left, 1, (int)(x * scale), (int)(33 * scale), 0);
        typeof(KeyboardForm).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, new object[] { mouse });
    }

    private static void ClickAction(KeyboardForm keyboard, int action)
    {
        float scale = (float)typeof(KeyboardForm).GetField("scale", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard);
        RectangleF bounds = ((RectangleF[])typeof(KeyboardForm).GetField("actions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard))[action];
        var preferences = (Preferences)typeof(KeyboardForm).GetField("preferences", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard);
        if (preferences.Compact && (action == 12 || action == 14)) bounds.Offset(-12, -54);
        var mouse = new MouseEventArgs(MouseButtons.Left, 1, (int)((bounds.Left + bounds.Width / 2) * scale), (int)((bounds.Top + bounds.Height / 2) * scale), 0);
        typeof(KeyboardForm).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(keyboard, new object[] { mouse });
    }

    private static void SetField(KeyboardForm keyboard, string name, object value)
    {
        typeof(KeyboardForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(keyboard, value);
    }

    private static void CheckPopupAnchor(KeyboardForm keyboard, ToolStripDropDown popup, int action)
    {
        float scale = (float)typeof(KeyboardForm).GetField("scale", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard);
        RectangleF button = ((RectangleF[])typeof(KeyboardForm).GetField("actions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard))[action];
        Point anchor = keyboard.PointToScreen(new Point((int)(button.Left * scale), (int)(button.Top * scale)));
        Rectangle area = Screen.FromPoint(anchor).WorkingArea;
        int expectedLeft = Math.Max(area.Left, Math.Min(anchor.X, area.Right - popup.Width));
        int expectedTop = Math.Max(area.Top, Math.Min(anchor.Y - popup.Height, area.Bottom - popup.Height));
        Check(popup.Visible && area.Contains(popup.Bounds) && Math.Abs(popup.Left - expectedLeft) <= 2 && Math.Abs(popup.Top - expectedTop) <= 2,
            "moved shortcut stays attached to its button: " + action + " expected=" + anchor + " popup=" + popup.Bounds);
    }
}
