using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

internal static class LdDecryptHotkey
{
    private const int HotkeyId = 0x4C44;
    private const int WmHotkey = 0x0312;
    private const int VkF8 = 0x77;
    private const int ApplyWindowReadyDelayMs = 3500;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint MouseeventfLeftdown = 0x0002;
    private const uint MouseeventfLeftup = 0x0004;
    private const uint MouseeventfRightdown = 0x0008;
    private const uint MouseeventfRightup = 0x0010;
    private const int MnGethmenu = 0x01E1;
    private const int WmClose = 0x0010;
    private const string StartupShortcutName = "Lvdun Auto Decryption.lnk";
    private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LdDecryptHotkey.log");

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuString(IntPtr hMenu, uint uIDItem, StringBuilder lpString, int nMaxCount, uint uFlag);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetMenuItemRect(IntPtr hWnd, IntPtr hMenu, uint uItem, out Rect rect);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private sealed class MenuHit
    {
        public IntPtr Hwnd;
        public IntPtr Hmenu;
        public int Index;
        public string Text;
        public Rect Rect;
    }

    private sealed class ExplorerTarget
    {
        public IntPtr Hwnd;
        public string Name;
        public Rect Rect;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
            return RunCli(args);

#if CLI
        PrintHelp();
        return 0;
#else
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var window = new HotkeyWindow())
        {
            Application.Run(window);
        }
        return 0;
#endif
    }

    private sealed class HotkeyWindow : Form
    {
        private readonly NotifyIcon tray;
        private volatile bool busy;

        public HotkeyWindow()
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Opacity = 0;

            tray = new NotifyIcon
            {
                Icon = SystemIcons.Shield,
                Text = "\u7eff\u76fe\u89e3\u5bc6 F8",
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip()
            };
            tray.ContextMenuStrip.Items.Add("\u8bbe\u7f6e\u5f00\u673a\u81ea\u542f", null, delegate { InstallStartup(true); });
            tray.ContextMenuStrip.Items.Add("\u53d6\u6d88\u5f00\u673a\u81ea\u542f", null, delegate { UninstallStartup(true); });
            tray.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            tray.ContextMenuStrip.Items.Add("\u9000\u51fa", null, delegate { Close(); });

            Load += delegate
            {
                RegisterHotKey(Handle, HotkeyId, 0, VkF8);
                Log("started");
                tray.ShowBalloonTip(1200, "\u7eff\u76fe\u89e3\u5bc6 F8", "\u5df2\u542f\u52a8\u3002\u9009\u4e2d\u6587\u4ef6\u540e\u6309 F8\u3002", ToolTipIcon.Info);
            };
            FormClosed += delegate
            {
                UnregisterHotKey(Handle, HotkeyId);
                tray.Visible = false;
                tray.Dispose();
                Log("stopped");
            };
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
            {
                if (!busy)
                {
                    busy = true;
                    var sourceWindow = GetForegroundWindow();
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        try
                        {
                            RunFlow(sourceWindow, false);
                        }
                        catch (Exception ex)
                        {
                            Log("error: " + ex);
                            SendEsc();
                            tray.ShowBalloonTip(3000, "\u7eff\u76fe\u89e3\u5bc6 F8", ex.Message, ToolTipIcon.Warning);
                        }
                        finally
                        {
                            busy = false;
                        }
                    });
                }
                return;
            }
            base.WndProc(ref m);
        }
    }

    private static void RunFlow(IntPtr sourceWindow, bool allowExplorerFallback)
    {
        Log("F8");
        var target = FindSelectedExplorerTarget(sourceWindow);
        if (target == null && allowExplorerFallback)
            target = FindMostRecentSelectedExplorerTarget();
        if (target == null)
            throw new InvalidOperationException("\u6ca1\u6709\u627e\u5230\u5f53\u524d\u8d44\u6e90\u7ba1\u7406\u5668\u4e2d\u660e\u786e\u9009\u4e2d\u7684\u5355\u4e2a\u6587\u4ef6\u3002\u8bf7\u5148\u5355\u51fb\u6587\u4ef6\uff0c\u518d\u6309 F8\u3002");

        Log("target: " + target.Name + " | hwnd=" + target.Hwnd + " | window=" + GetTitle(target.Hwnd));
        CloseExistingApplyWindows();
        SendEsc();
        Thread.Sleep(120);
        var menu = OpenMenuAndFindEncryptionMenu(target);
        if (menu == null)
            throw new InvalidOperationException("\u6ca1\u6709\u627e\u5230\u201c\u52a0\u5bc6\u83dc\u5355\u201d\u3002\u8bf7\u786e\u8ba4\u6587\u4ef6\u5df2\u9009\u4e2d\uff0c\u4e14\u53f3\u952e\u83dc\u5355\u91cc\u80fd\u770b\u5230\u5b83\u3002");

        Log("found menu: " + menu.Text);
        Hover(menu);
        Thread.Sleep(500);
        SendKey(0x27); // Right arrow, opens submenu for classic menus.
        Thread.Sleep(300);

        var decrypt = WaitForPopupMenuItem(new[] { "\u7533\u8bf7\u89e3\u5bc6", "\u89e3\u5bc6\u7533\u8bf7", "\u89e3\u5bc6" }, 3500);
        if (decrypt == null)
            throw new InvalidOperationException("\u627e\u5230\u4e86\u201c\u52a0\u5bc6\u83dc\u5355\u201d\uff0c\u4f46\u6ca1\u6709\u627e\u5230\u201c\u7533\u8bf7\u89e3\u5bc6\u201d\u3002");

        Log("found decrypt: " + decrypt.Text);
        Click(decrypt);
        Log("decrypt clicked");

        var applyWindow = WaitForWindowTitleContains(new[] { "\u65b0\u5efa\u7533\u8bf7", "\u6587\u4ef6\u89e3\u5bc6\u7533\u8bf7" }, 10000);
        if (applyWindow == IntPtr.Zero)
        {
            Log("apply window not found");
            return;
        }

        Log("apply window found");
        Thread.Sleep(ApplyWindowReadyDelayMs);
        ClickSendApplyButton(applyWindow);
        Log("send apply clicked");
    }

    private static int RunCli(string[] args)
    {
        var command = args[0].Trim().ToLowerInvariant();
        try
        {
            switch (command)
            {
                case "--once":
                case "once":
                    Console.WriteLine("Running one decrypt-apply flow. Select a file in Explorer before using this command.");
                    RunFlow(GetForegroundWindow(), true);
                    Console.WriteLine("Done.");
                    return 0;

                case "--install-startup":
                case "install-startup":
                    InstallStartup(false);
                    Console.WriteLine("Startup enabled.");
                    return 0;

                case "--uninstall-startup":
                case "uninstall-startup":
                    UninstallStartup(false);
                    Console.WriteLine("Startup disabled.");
                    return 0;

                case "--status":
                case "status":
                    Console.WriteLine("Startup: " + (IsStartupInstalled() ? "enabled" : "disabled"));
                    Console.WriteLine("Log: " + LogPath);
                    return 0;

                case "--help":
                case "-h":
                case "/?":
                case "help":
                    PrintHelp();
                    return 0;

                default:
                    Console.Error.WriteLine("Unknown command: " + args[0]);
                    PrintHelp();
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Log("cli error: " + ex);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Lvdun Auto Decryption CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --once              Run one F8 flow for the selected Explorer file");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --install-startup   Enable startup");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --uninstall-startup Disable startup");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --status            Show startup status and log path");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --help              Show help");
        Console.WriteLine();
        Console.WriteLine("For normal daily use, run LdDecryptHotkey.exe and press F8 in Explorer.");
    }

    private static MenuHit OpenMenuAndFindEncryptionMenu(ExplorerTarget target)
    {
        var needles = new[] { "\u52a0\u5bc6\u83dc\u5355" };

        Log("open menu: targeted shift+rightclick");
        SendEsc();
        Thread.Sleep(100);
        SetForegroundWindow(target.Hwnd);
        Thread.Sleep(150);
        MoveCursorToTarget(target);
        SendShiftRightClick();
        var menu = WaitForPopupMenuItem(needles, 2500);
        if (menu != null) return menu;

        // The targeted right-click also gives the exact item keyboard focus.
        Log("open menu fallback: shift+f10");
        SendEsc();
        Thread.Sleep(100);
        SetForegroundWindow(target.Hwnd);
        SendShiftF10();
        menu = WaitForPopupMenuItem(needles, 2500);
        if (menu != null) return menu;

        Log("open menu fallback: shift+appskey");
        SendEsc();
        Thread.Sleep(100);
        SetForegroundWindow(target.Hwnd);
        SendShiftAppsKey();
        return WaitForPopupMenuItem(needles, 2500);
    }

    private static ExplorerTarget FindSelectedExplorerTarget(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsExplorerWindow(hwnd))
            return null;

        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var items = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
            ExplorerTarget result = null;
            var selectedCount = 0;

            for (var i = 0; i < items.Count; i++)
            {
                object pattern;
                if (!items[i].TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
                    continue;
                if (!((SelectionItemPattern)pattern).Current.IsSelected || items[i].Current.IsOffscreen)
                    continue;

                var bounds = items[i].Current.BoundingRectangle;
                if (bounds.IsEmpty || bounds.Width < 4 || bounds.Height < 4)
                    continue;

                selectedCount++;
                result = new ExplorerTarget
                {
                    Hwnd = hwnd,
                    Name = SafeName(items[i]),
                    Rect = new Rect
                    {
                        Left = (int)bounds.Left,
                        Top = (int)bounds.Top,
                        Right = (int)bounds.Right,
                        Bottom = (int)bounds.Bottom
                    }
                };
            }

            if (selectedCount == 1)
                return result;

            if (selectedCount > 1)
                Log("target rejected: multiple selected items in hwnd=" + hwnd);
        }
        catch (Exception ex)
        {
            Log("target detection error: " + ex.Message);
        }
        return null;
    }

    private static ExplorerTarget FindMostRecentSelectedExplorerTarget()
    {
        ExplorerTarget result = null;
        EnumWindows(delegate (IntPtr hwnd, IntPtr lParam)
        {
            result = FindSelectedExplorerTarget(hwnd);
            return result == null;
        }, IntPtr.Zero);
        return result;
    }

    private static bool IsExplorerWindow(IntPtr hwnd)
    {
        var className = GetWindowClass(hwnd);
        return className == "CabinetWClass" || className == "ExploreWClass";
    }

    private static void MoveCursorToTarget(ExplorerTarget target)
    {
        var width = target.Rect.Right - target.Rect.Left;
        var x = target.Rect.Left + Math.Min(120, Math.Max(4, width / 2));
        var y = (target.Rect.Top + target.Rect.Bottom) / 2;
        SetCursorPos(x, y);
        Thread.Sleep(100);
    }

    private static AutomationElement WaitForButtonContains(string[] needles, int timeoutMs)
    {
        return WaitForElementContains(ControlType.Button, needles, timeoutMs);
    }

    private static MenuHit WaitForPopupMenuItem(string[] needles, int timeoutMs)
    {
        var deadline = Environment.TickCount + timeoutMs;
        while (Environment.TickCount < deadline)
        {
            var hit = FindPopupMenuItem(needles);
            if (hit != null) return hit;
            Thread.Sleep(80);
        }
        LogVisiblePopupMenus();
        return null;
    }

    private static MenuHit FindPopupMenuItem(string[] needles)
    {
        MenuHit result = null;
        EnumWindows(delegate (IntPtr hwnd, IntPtr lParam)
        {
            if (!IsWindowVisible(hwnd) || GetWindowClass(hwnd) != "#32768")
                return true;

            var hmenu = SendMessage(hwnd, MnGethmenu, IntPtr.Zero, IntPtr.Zero);
            if (hmenu == IntPtr.Zero)
                return true;

            var count = GetMenuItemCount(hmenu);
            for (var i = 0; i < count; i++)
            {
                var text = GetMenuText(hmenu, i);
                var normalized = Normalize(text);
                foreach (var needle in needles)
                {
                    if (normalized.IndexOf(Normalize(needle), StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Rect rect;
                        GetMenuItemRect(hwnd, hmenu, (uint)i, out rect);
                        result = new MenuHit { Hwnd = hwnd, Hmenu = hmenu, Index = i, Text = text, Rect = rect };
                        return false;
                    }
                }
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static void LogVisiblePopupMenus()
    {
        EnumWindows(delegate (IntPtr hwnd, IntPtr lParam)
        {
            if (!IsWindowVisible(hwnd) || GetWindowClass(hwnd) != "#32768")
                return true;

            var hmenu = SendMessage(hwnd, MnGethmenu, IntPtr.Zero, IntPtr.Zero);
            if (hmenu == IntPtr.Zero)
                return true;

            var count = GetMenuItemCount(hmenu);
            for (var i = 0; i < count; i++)
                Log("popup item: " + GetMenuText(hmenu, i));
            return true;
        }, IntPtr.Zero);
    }

    private static string GetWindowClass(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var buffer = new StringBuilder(512);
        GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static IntPtr WaitForWindowTitleContains(string[] needles, int timeoutMs)
    {
        var deadline = Environment.TickCount + timeoutMs;
        while (Environment.TickCount < deadline)
        {
            var found = FindWindowTitleContains(needles);
            if (found != IntPtr.Zero) return found;
            Thread.Sleep(120);
        }
        return IntPtr.Zero;
    }

    private static void CloseExistingApplyWindows()
    {
        var windows = FindWindowsTitleContains(new[] { "\u65b0\u5efa\u7533\u8bf7", "\u6587\u4ef6\u89e3\u5bc6\u7533\u8bf7" });
        if (windows.Count == 0)
            return;

        foreach (var hwnd in windows)
        {
            Log("closing stale apply window: " + GetTitle(hwnd));
            SendMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
        }

        var deadline = Environment.TickCount + 2500;
        while (Environment.TickCount < deadline)
        {
            if (FindWindowsTitleContains(new[] { "\u65b0\u5efa\u7533\u8bf7", "\u6587\u4ef6\u89e3\u5bc6\u7533\u8bf7" }).Count == 0)
                return;
            Thread.Sleep(100);
        }
    }

    private static IntPtr FindWindowTitleContains(string[] needles)
    {
        var windows = FindWindowsTitleContains(needles);
        return windows.Count > 0 ? windows[0] : IntPtr.Zero;
    }

    private static System.Collections.Generic.List<IntPtr> FindWindowsTitleContains(string[] needles)
    {
        var results = new System.Collections.Generic.List<IntPtr>();
        EnumWindows(delegate (IntPtr hwnd, IntPtr lParam)
        {
            if (!IsWindowVisible(hwnd))
                return true;

            var title = Normalize(GetTitle(hwnd));
            foreach (var needle in needles)
            {
                if (title.IndexOf(Normalize(needle), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    results.Add(hwnd);
                    return true;
                }
            }
            return true;
        }, IntPtr.Zero);
        return results;
    }

    private static string GetMenuText(IntPtr hmenu, int index)
    {
        var buffer = new StringBuilder(512);
        GetMenuString(hmenu, (uint)index, buffer, buffer.Capacity, 0x00000400);
        return buffer.ToString();
    }

    private static AutomationElement WaitForElementContains(ControlType type, string[] needles, int timeoutMs)
    {
        var deadline = Environment.TickCount + timeoutMs;
        while (Environment.TickCount < deadline)
        {
            var all = AutomationElement.RootElement.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, type));

            for (var i = 0; i < all.Count; i++)
            {
                var name = Normalize(SafeName(all[i]));
                foreach (var needle in needles)
                {
                    if (name.IndexOf(Normalize(needle), StringComparison.OrdinalIgnoreCase) >= 0)
                        return all[i];
                }
            }
            Thread.Sleep(80);
        }
        return null;
    }

    private static string Normalize(string value)
    {
        if (value == null) return "";
        return value.Replace(" ", "").Replace("\t", "").Replace("&", "").Replace("(", "").Replace(")", "");
    }

    private static string SafeName(AutomationElement element)
    {
        try { return element.Current.Name ?? ""; }
        catch { return ""; }
    }

    private static void Hover(MenuHit hit)
    {
        SetCursorPos((hit.Rect.Left + hit.Rect.Right) / 2, (hit.Rect.Top + hit.Rect.Bottom) / 2);
    }

    private static void Click(MenuHit hit)
    {
        SetCursorPos((hit.Rect.Left + hit.Rect.Right) / 2, (hit.Rect.Top + hit.Rect.Bottom) / 2);
        Thread.Sleep(80);
        mouse_event(MouseeventfLeftdown, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MouseeventfLeftup, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(350);
    }

    private static void ClickSendApplyButton(IntPtr hwnd)
    {
        Rect rect;
        if (!GetWindowRect(hwnd, out rect))
            return;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            return;

        // The Green Shield application uses a fixed bottom-right button layout.
        var x = rect.Right - 70;
        var y = rect.Bottom - 46;
        SetCursorPos(x, y);
        Thread.Sleep(150);
        mouse_event(MouseeventfLeftdown, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MouseeventfLeftup, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(350);
    }

    private static void ClickOrInvoke(AutomationElement element)
    {
        object pattern;
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
        {
            ((InvokePattern)pattern).Invoke();
            Thread.Sleep(350);
            return;
        }

        var rect = element.Current.BoundingRectangle;
        if (!rect.IsEmpty)
        {
            SetCursorPos((int)(rect.Left + rect.Width / 2), (int)(rect.Top + rect.Height / 2));
            Thread.Sleep(80);
            mouse_event(MouseeventfLeftdown, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MouseeventfLeftup, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(350);
        }
    }

    private static void SendShiftF10()
    {
        keybd_event(0x10, 0, 0, UIntPtr.Zero);
        SendKey(0x79);
        keybd_event(0x10, 0, KeyeventfKeyup, UIntPtr.Zero);
    }

    private static void SendShiftAppsKey()
    {
        keybd_event(0x10, 0, 0, UIntPtr.Zero);
        SendKey(0x5D);
        keybd_event(0x10, 0, KeyeventfKeyup, UIntPtr.Zero);
    }

    private static void SendShiftRightClick()
    {
        keybd_event(0x10, 0, 0, UIntPtr.Zero);
        Thread.Sleep(80);
        mouse_event(MouseeventfRightdown, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MouseeventfRightup, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(80);
        keybd_event(0x10, 0, KeyeventfKeyup, UIntPtr.Zero);
    }

    private static void SendEsc()
    {
        SendKey(0x1B);
    }

    private static void SendKey(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KeyeventfKeyup, UIntPtr.Zero);
    }

    private static void InstallStartup(bool showMessage)
    {
        try
        {
            var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupShortcutName);
            var script = string.Format(
                "$ws=New-Object -ComObject WScript.Shell; $s=$ws.CreateShortcut('{0}'); $s.TargetPath='{1}'; $s.WorkingDirectory='{2}'; $s.Save()",
                shortcutPath.Replace("'", "''"),
                Application.ExecutablePath.Replace("'", "''"),
                AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\').Replace("'", "''"));
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + script + "\"",
                CreateNoWindow = true,
                UseShellExecute = false
            }).WaitForExit();
            Log("startup installed");
            if (showMessage)
                MessageBox.Show("\u5df2\u8bbe\u7f6e\u5f00\u673a\u81ea\u542f\u3002", "\u7eff\u76fe\u89e3\u5bc6 F8", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("startup install error: " + ex);
            if (showMessage)
                MessageBox.Show("\u8bbe\u7f6e\u5f00\u673a\u81ea\u542f\u5931\u8d25\uff1a" + ex.Message, "\u7eff\u76fe\u89e3\u5bc6 F8", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                throw;
        }
    }

    private static void UninstallStartup(bool showMessage)
    {
        try
        {
            var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupShortcutName);
            if (File.Exists(shortcutPath))
                File.Delete(shortcutPath);
            Log("startup uninstalled");
            if (showMessage)
                MessageBox.Show("\u5df2\u53d6\u6d88\u5f00\u673a\u81ea\u542f\u3002", "\u7eff\u76fe\u89e3\u5bc6 F8", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("startup uninstall error: " + ex);
            if (showMessage)
                MessageBox.Show("\u53d6\u6d88\u5f00\u673a\u81ea\u542f\u5931\u8d25\uff1a" + ex.Message, "\u7eff\u76fe\u89e3\u5bc6 F8", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                throw;
        }
    }

    private static bool IsStartupInstalled()
    {
        var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupShortcutName);
        return File.Exists(shortcutPath);
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + message + Environment.NewLine);
        }
        catch { }
    }
}
