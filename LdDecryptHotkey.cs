using System;
using System.Collections.Generic;
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
    private const int DirectApplyReadyDelayMs = 900;
    private const int LdCommandBufferSize = 0x20C;
    private const int LdCommandDataSize = 512;
    private const uint MouseeventfLeftdown = 0x0002;
    private const uint MouseeventfLeftup = 0x0004;
    private const int WmClose = 0x0010;
    private const string StartupShortcutName = "Lvdun Auto Decryption.lnk";
    private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LdDecryptHotkey.log");
    private static readonly object LdPlugSync = new object();
    private static bool ldPlugInitialized;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

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

    [DllImport(@"C:\Inetpub\ftproot\Tipray\LdTerm\LdMenuPlug.dll", EntryPoint = "DllSysPlugInit", CallingConvention = CallingConvention.Cdecl)]
    private static extern void LdMenuPlugInit();

    [DllImport(@"C:\Inetpub\ftproot\Tipray\LdTerm\LdMenuPlug.dll", EntryPoint = "DllSysPlugRelease", CallingConvention = CallingConvention.Cdecl)]
    private static extern void LdMenuPlugRelease();

    [DllImport(@"C:\Inetpub\ftproot\Tipray\LdTerm\LdMenuPlug.dll", EntryPoint = "DllSysPlugGetMenuType", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool LdMenuPlugGetMenuType(byte type);

    [DllImport(@"C:\Inetpub\ftproot\Tipray\LdTerm\LdMenuPlug.dll", EntryPoint = "DllSysPlugSetCmdInfo", CallingConvention = CallingConvention.Cdecl)]
    private static extern void LdMenuPlugSetCmdInfo(IntPtr command);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private sealed class ExplorerTarget
    {
        public IntPtr Hwnd;
        public string Name;
        public int SelectedCount;
        public List<string> Paths;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
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
        finally
        {
            ReleaseLdMenuPlug();
        }
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
                Text = "\u7eff\u76fe\u5feb\u901f\u7533\u8bf7 F8",
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
                tray.ShowBalloonTip(1200, "\u7eff\u76fe\u5feb\u901f\u7533\u8bf7 F8", "\u5df2\u542f\u52a8\u3002\u9009\u4e2d\u6587\u4ef6\u540e\u6309 F8\uff0c\u76f4\u63a5\u8c03\u7528\u7eff\u76fe\u672c\u5730\u7533\u8bf7\u3002", ToolTipIcon.Info);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { EnsureLdMenuPlugInitialized(); Log("direct plug ready"); }
                    catch (Exception ex) { Log("direct plug warmup error: " + ex.Message); }
                });
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
                            RunDirectApprovalFlow(sourceWindow, false, true);
                        }
                        catch (Exception ex)
                        {
                            Log("error: " + ex);
                            tray.ShowBalloonTip(3000, "\u7eff\u76fe\u5feb\u901f\u7533\u8bf7 F8", ex.Message, ToolTipIcon.Warning);
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

    private static void RunDirectApprovalFlow(IntPtr sourceWindow, bool allowExplorerFallback, bool submit)
    {
        Log("F8 direct local signal");
        Log("source: hwnd=" + sourceWindow + " | class=" + GetWindowClass(sourceWindow) + " | window=" + GetTitle(sourceWindow));
        var target = FindSelectedExplorerTarget(sourceWindow);
        if (target == null && allowExplorerFallback)
            target = FindMostRecentSelectedExplorerTarget();
        if (target == null || target.Paths == null || target.Paths.Count == 0)
            throw new InvalidOperationException("\u6ca1\u6709\u627e\u5230\u5f53\u524d\u8d44\u6e90\u7ba1\u7406\u5668\u6216\u684c\u9762\u4e2d\u660e\u786e\u9009\u4e2d\u7684\u6587\u4ef6\u3002\u8bf7\u5148\u9009\u4e2d\u76ee\u6807\uff0c\u518d\u6309 F8\u3002");

        Log("target: " + target.Name + " | count=" + target.SelectedCount + " | hwnd=" + target.Hwnd);

        foreach (var path in target.Paths)
        {
            if (Directory.Exists(path))
                throw new InvalidOperationException("\u76f4\u8fde\u6a21\u5f0f\u5f53\u524d\u53ea\u652f\u6301\u6587\u4ef6\uff0c\u8bf7\u8fdb\u5165\u6587\u4ef6\u5939\u540e\u9009\u4e2d\u6587\u4ef6\u3002");
            if (!File.Exists(path))
                throw new FileNotFoundException("\u9009\u4e2d\u7684\u6587\u4ef6\u4e0d\u5b58\u5728\u3002", path);
        }

        CloseExistingApplyWindows();
        SendOfficialDecryptSignal(target.Paths);
        Log("direct signal sent: " + target.Paths.Count + " file(s)");

        var applyWindow = WaitForWindowTitleContains(new[] { "\u65b0\u5efa\u7533\u8bf7", "\u6587\u4ef6\u89e3\u5bc6\u7533\u8bf7" }, 10000);
        if (applyWindow == IntPtr.Zero)
            throw new InvalidOperationException("\u7eff\u76fe\u5df2\u63a5\u6536\u672c\u5730\u547d\u4ee4\uff0c\u4f46\u6ca1\u6709\u6253\u5f00\u7533\u8bf7\u7a97\u53e3\u3002");

        Log("direct apply window found");
        if (!submit) return;

        var sendButton = WaitForButtonInWindow(applyWindow,
            new[] { "\u53d1\u9001\u7533\u8bf7", "\u63d0\u4ea4\u7533\u8bf7", "\u53d1\u9001", "\u63d0\u4ea4" },
            DirectApplyReadyDelayMs);
        if (sendButton != null)
            ClickOrInvoke(sendButton);
        else
            ClickSendApplyButton(applyWindow);
        Log("direct send apply clicked");
    }

    private static void SendOfficialDecryptSignal(List<string> paths)
    {
        const string plugPath = @"C:\Inetpub\ftproot\Tipray\LdTerm\LdMenuPlug.dll";
        if (!File.Exists(plugPath))
            throw new FileNotFoundException("\u6ca1\u6709\u627e\u5230\u7eff\u76fe\u672c\u5730\u83dc\u5355\u7ec4\u4ef6\u3002", plugPath);

        lock (LdPlugSync)
        {
            EnsureLdMenuPlugInitializedUnsafe();
            if (!LdMenuPlugGetMenuType(10))
                throw new InvalidOperationException("\u5f53\u524d\u7eff\u76fe\u7b56\u7565\u672a\u542f\u7528\u201c\u7533\u8bf7\u89e3\u5bc6\u201d\u83dc\u5355\u3002");

            for (var index = 0; index < paths.Count; index++)
            {
                var isLast = index == paths.Count - 1;
                SendLdCommand(1, Encoding.Default.GetBytes(paths[index] + "\0"), isLast);
                SendLdCommand(0x12, Encoding.Unicode.GetBytes(paths[index]), isLast);
            }
        }
    }

    private static void EnsureLdMenuPlugInitialized()
    {
        lock (LdPlugSync) EnsureLdMenuPlugInitializedUnsafe();
    }

    private static void EnsureLdMenuPlugInitializedUnsafe()
    {
        if (ldPlugInitialized) return;
        LdMenuPlugInit();
        ldPlugInitialized = true;
    }

    private static void ReleaseLdMenuPlug()
    {
        lock (LdPlugSync)
        {
            if (!ldPlugInitialized) return;
            try { LdMenuPlugRelease(); }
            catch (Exception ex) { Log("direct plug release error: " + ex.Message); }
            ldPlugInitialized = false;
        }
    }

    private static void SendLdCommand(int command, byte[] data, bool isLast)
    {
        if (data.Length > LdCommandDataSize)
            throw new PathTooLongException("\u7eff\u76fe\u672c\u5730\u547d\u4ee4\u53ea\u652f\u6301 512 \u5b57\u8282\u4ee5\u5185\u7684\u8def\u5f84\u3002");

        var pointer = Marshal.AllocHGlobal(LdCommandBufferSize);
        try
        {
            for (var offset = 0; offset < LdCommandBufferSize; offset += 4)
                Marshal.WriteInt32(pointer, offset, 0);
            Marshal.WriteInt32(pointer, 0, command);
            Marshal.Copy(data, 0, new IntPtr(pointer.ToInt64() + 4), data.Length);
            Marshal.WriteInt32(pointer, 0x204, isLast ? 1 : 0);
            LdMenuPlugSetCmdInfo(pointer);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
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
                    Console.WriteLine("Sending the official local decrypt-application command for selected files.");
                    RunDirectApprovalFlow(GetForegroundWindow(), true, true);
                    return 0;

                case "--prepare-once":
                    Console.WriteLine("Opening the official application window without submitting it.");
                    var prepareWindow = args.Length > 1 ? new IntPtr(long.Parse(args[1])) : GetForegroundWindow();
                    RunDirectApprovalFlow(prepareWindow, args.Length == 1, false);
                    return 0;

                case "--list-selected":
                    var requestedWindow = args.Length > 1 ? new IntPtr(long.Parse(args[1])) : GetForegroundWindow();
                    var selectedTarget = FindSelectedExplorerTarget(requestedWindow);
                    if (selectedTarget == null && args.Length == 1) selectedTarget = FindMostRecentSelectedExplorerTarget();
                    if (selectedTarget == null || selectedTarget.Paths == null || selectedTarget.Paths.Count == 0)
                        throw new InvalidOperationException("No selected Explorer files were found.");
                    foreach (var path in selectedTarget.Paths) Console.WriteLine(path);
                    return 0;

                case "--probe-direct":
                    EnsureLdMenuPlugInitialized();
                    var directAvailable = LdMenuPlugGetMenuType(10);
                    Console.WriteLine("Direct local application: " + (directAvailable ? "available" : "disabled by policy"));
                    return directAvailable ? 0 : 1;

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
        Console.WriteLine("  LdDecryptHotkeyCli.exe --once              Run the direct local application flow");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --prepare-once [HWND]  Open the application window without submitting");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --list-selected [HWND]  Inspect selected paths without copying");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --probe-direct      Check the local Green Shield interface");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --install-startup   Enable startup");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --uninstall-startup Disable startup");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --status            Show startup status and log path");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --help              Show help");
        Console.WriteLine();
        Console.WriteLine("For normal daily use, run LdDecryptHotkey.exe and press F8 in Explorer.");
    }

    private static ExplorerTarget FindSelectedExplorerTarget(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsExplorerWindow(hwnd))
            return null;

        try
        {
            var shellPaths = GetExplorerSelectedPaths(hwnd);
            var root = AutomationElement.FromHandle(hwnd);
            var items = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
            var selectedCount = 0;
            var selectedNames = new List<string>();

            for (var i = 0; i < items.Count; i++)
            {
                object pattern;
                if (!items[i].TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
                    continue;
                if (!((SelectionItemPattern)pattern).Current.IsSelected || items[i].Current.IsOffscreen)
                    continue;

                selectedCount++;
                selectedNames.Add(SafeName(items[i]));
            }

            if (selectedCount > 0)
            {
                return new ExplorerTarget
                {
                    Hwnd = hwnd,
                    SelectedCount = selectedCount,
                    Name = BuildSelectionSummary(selectedNames),
                    Paths = shellPaths.Count > 0 ? shellPaths : GetSelectedPaths(hwnd, selectedNames)
                };
            }

            // Explorer's automation tree can report selected items as off-screen even
            // though the Shell selection is valid. Direct mode only needs exact paths.
            if (shellPaths.Count > 0)
            {
                var pathNames = new List<string>();
                foreach (var path in shellPaths) pathNames.Add(Path.GetFileName(path));
                return new ExplorerTarget
                {
                    Hwnd = hwnd,
                    SelectedCount = shellPaths.Count,
                    Name = BuildSelectionSummary(pathNames),
                    Paths = shellPaths
                };
            }
        }
        catch (Exception ex)
        {
            Log("target detection error: " + ex.Message);
        }
        return null;
    }

    private static List<string> GetSelectedPaths(IntPtr hwnd, List<string> selectedNames)
    {
        var paths = GetExplorerSelectedPaths(hwnd);
        if (paths.Count > 0)
            return paths;

        var className = GetWindowClass(hwnd);
        if (className != "Progman" && className != "WorkerW")
            return paths;

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        foreach (var name in selectedNames)
        {
            var path = Path.Combine(desktop, name);
            if (File.Exists(path) || Directory.Exists(path))
                paths.Add(path);
        }
        return paths;
    }

    private static List<string> GetExplorerSelectedPaths(IntPtr hwnd)
    {
        var result = new List<string>();
        object shell = null;
        object windows = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return result;
            shell = Activator.CreateInstance(shellType);
            windows = shell.GetType().InvokeMember("Windows", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
            var count = Convert.ToInt32(windows.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, windows, null));
            for (var i = 0; i < count; i++)
            {
                object window = null;
                object document = null;
                object selectedItems = null;
                try
                {
                    window = windows.GetType().InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, windows, new object[] { i });
                    if (window == null) continue;
                    var windowHandle = Convert.ToInt64(window.GetType().InvokeMember("HWND", System.Reflection.BindingFlags.GetProperty, null, window, null));
                    if (windowHandle != hwnd.ToInt64()) continue;

                    document = window.GetType().InvokeMember("Document", System.Reflection.BindingFlags.GetProperty, null, window, null);
                    selectedItems = document.GetType().InvokeMember("SelectedItems", System.Reflection.BindingFlags.InvokeMethod, null, document, null);
                    var selectedCount = Convert.ToInt32(selectedItems.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, selectedItems, null));
                    for (var itemIndex = 0; itemIndex < selectedCount; itemIndex++)
                    {
                        object item = null;
                        try
                        {
                            item = selectedItems.GetType().InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, selectedItems, new object[] { itemIndex });
                            var path = Convert.ToString(item.GetType().InvokeMember("Path", System.Reflection.BindingFlags.GetProperty, null, item, null));
                            if (!string.IsNullOrWhiteSpace(path)) result.Add(path);
                        }
                        finally { ReleaseComObject(item); }
                    }
                    break;
                }
                catch (Exception ex)
                {
                    Log("selected path lookup item error: " + ex.Message);
                }
                finally
                {
                    ReleaseComObject(selectedItems);
                    ReleaseComObject(document);
                    ReleaseComObject(window);
                }
            }
        }
        catch (Exception ex)
        {
            Log("selected path lookup error: " + ex.Message);
        }
        finally
        {
            ReleaseComObject(windows);
            ReleaseComObject(shell);
        }
        return result;
    }

    private static void ReleaseComObject(object value)
    {
        if (value != null && Marshal.IsComObject(value))
        {
            try { Marshal.FinalReleaseComObject(value); } catch { }
        }
    }

    private static string BuildSelectionSummary(System.Collections.Generic.List<string> names)
    {
        if (names.Count == 1)
            return names[0];

        var summary = new StringBuilder();
        summary.Append(names.Count).Append(" selected: ");
        var shown = Math.Min(names.Count, 3);
        for (var i = 0; i < shown; i++)
        {
            if (i > 0) summary.Append(" | ");
            summary.Append(names[i]);
        }
        if (names.Count > shown)
            summary.Append(" | ...");
        return summary.ToString();
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
        if (className == "CabinetWClass" || className == "ExploreWClass" || className == "Progman")
            return true;

        // Windows can host the desktop list view under a top-level WorkerW.
        return className == "WorkerW" &&
            FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
    }

    private static AutomationElement FindButtonInWindow(IntPtr hwnd, string[] needles)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var buttons = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            for (var i = 0; i < buttons.Count; i++)
            {
                if (!buttons[i].Current.IsEnabled || buttons[i].Current.IsOffscreen)
                    continue;
                var name = Normalize(SafeName(buttons[i]));
                foreach (var needle in needles)
                {
                    if (name.IndexOf(Normalize(needle), StringComparison.OrdinalIgnoreCase) >= 0)
                        return buttons[i];
                }
            }
        }
        catch (Exception ex)
        {
            Log("send button lookup error: " + ex.Message);
        }
        return null;
    }

    private static AutomationElement WaitForButtonInWindow(IntPtr hwnd, string[] needles, int timeoutMs)
    {
        var deadline = Environment.TickCount + timeoutMs;
        while (Environment.TickCount < deadline)
        {
            var button = FindButtonInWindow(hwnd, needles);
            if (button != null) return button;
            Thread.Sleep(80);
        }
        return null;
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
