using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using System.Web.Script.Serialization;

internal static class LdDecryptHotkey
{
    private const int HotkeyId = 0x4C44;
    private const int WmHotkey = 0x0312;
    private const int VkF8 = 0x77;
    private const int DirectApplyReadyDelayMs = 900;
    private const int LdCommandBufferSize = 0x20C;
    private const int LdCommandDataSize = 512;
    private const int MaxTrackedFiles = 10000;
    private const string McpPipeName = "GreenShieldQuickApply.Mcp.v1";
    private const string ServiceVersion = "1.4.0";
    private const uint MouseeventfLeftdown = 0x0002;
    private const uint MouseeventfLeftup = 0x0004;
    private const string StartupShortcutName = "Lvdun Auto Decryption.lnk";
    private const string LdMenuPlugPath = @"C:\Inetpub\ftproot\Tipray\LdTerm\LdMenuPlug.dll";
    private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LdDecryptHotkey.log");
    private static readonly object LdPlugSync = new object();
    private static readonly object LogSync = new object();
    private static readonly object DecryptionOperationSync = new object();
    private static bool ldPlugInitialized;
    private static volatile bool pipeServerStopping;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

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
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport(LdMenuPlugPath, EntryPoint = "DllSysPlugInit", CallingConvention = CallingConvention.Cdecl)]
    private static extern void LdMenuPlugInit();

    [DllImport(LdMenuPlugPath, EntryPoint = "DllSysPlugRelease", CallingConvention = CallingConvention.Cdecl)]
    private static extern void LdMenuPlugRelease();

    [DllImport(LdMenuPlugPath, EntryPoint = "DllSysPlugGetMenuType", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool LdMenuPlugGetMenuType(byte type);

    [DllImport(LdMenuPlugPath, EntryPoint = "DllSysPlugSetCmdInfo", CallingConvention = CallingConvention.Cdecl)]
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
        private bool hotkeyRegistered;

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
                hotkeyRegistered = RegisterHotKey(Handle, HotkeyId, 0, VkF8);
                if (!hotkeyRegistered)
                {
                    Log("F8 registration failed");
                    MessageBox.Show(
                        "F8 \u5feb\u6377\u952e\u6ce8\u518c\u5931\u8d25\uff0c\u53ef\u80fd\u5df2\u6709\u53e6\u4e00\u4e2a\u7a0b\u5e8f\u5360\u7528 F8\uff0c\u6216\u672c\u5de5\u5177\u5df2\u7ecf\u5728\u8fd0\u884c\u3002",
                        "\u7eff\u76fe\u5feb\u901f\u7533\u8bf7 F8",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    Close();
                    return;
                }
                Log("started");
                StartMcpPipeServer();
                tray.ShowBalloonTip(1200, "\u7eff\u76fe\u5feb\u901f\u7533\u8bf7 F8", "\u5df2\u542f\u52a8\u3002\u9009\u4e2d\u6587\u4ef6\u6216\u6587\u4ef6\u5939\u540e\u6309 F8\uff0c\u76f4\u63a5\u8c03\u7528\u7eff\u76fe\u672c\u5730\u7533\u8bf7\u3002", ToolTipIcon.Info);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { EnsureLdMenuPlugInitialized(); Log("direct plug ready"); }
                    catch (Exception ex) { Log("direct plug warmup error: " + ex.Message); }
                });
            };
            FormClosed += delegate
            {
                pipeServerStopping = true;
                if (hotkeyRegistered)
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
                            ShowWarning(ex.Message);
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

        private void ShowWarning(string message)
        {
            if (IsDisposed || Disposing)
                return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(ShowWarning), message); }
                catch (InvalidOperationException) { }
                return;
            }
            tray.ShowBalloonTip(3000, "\u7eff\u76fe\u5feb\u901f\u7533\u8bf7 F8", message, ToolTipIcon.Warning);
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
            throw new InvalidOperationException("\u6ca1\u6709\u627e\u5230\u5f53\u524d\u8d44\u6e90\u7ba1\u7406\u5668\u6216\u684c\u9762\u4e2d\u660e\u786e\u9009\u4e2d\u7684\u6587\u4ef6\u6216\u6587\u4ef6\u5939\u3002\u8bf7\u5148\u9009\u4e2d\u76ee\u6807\uff0c\u518d\u6309 F8\u3002");

        var paths = ValidateTargets(target.Paths);
        Log("target: " + target.Name + " | count=" + paths.Count + " | hwnd=" + target.Hwnd);
        RunDirectApprovalForPaths(paths, submit);
    }

    private static void RunDirectApprovalForPaths(List<string> rawPaths, bool submit)
    {
        var paths = ValidateTargets(rawPaths);
        lock (DecryptionOperationSync)
        {
            if (FindApprovalWindows().Count > 0)
                throw new InvalidOperationException("\u5df2\u6709\u7eff\u76fe\u89e3\u5bc6\u7533\u8bf7\u7a97\u53e3\u672a\u5904\u7406\u3002\u8bf7\u5148\u53d1\u9001\u6216\u5173\u95ed\u5b83\uff0c\u518d\u91cd\u8bd5\uff0c\u4ee5\u514d\u628a\u65b0\u76ee\u6807\u52a0\u5165\u9519\u8bef\u7684\u7533\u8bf7\u3002");

            SendOfficialDecryptSignal(paths);
            Log("direct signal sent: " + paths.Count + " item(s)");

            var applyWindow = WaitForApprovalWindow(10000);
            if (applyWindow == IntPtr.Zero)
                throw new InvalidOperationException("\u7eff\u76fe\u5df2\u63a5\u6536\u672c\u5730\u547d\u4ee4\uff0c\u4f46\u6ca1\u6709\u6253\u5f00\u7533\u8bf7\u7a97\u53e3\u3002");

            Log("direct apply window found");
            if (!submit) return;

            var sendButton = WaitForButtonInWindow(applyWindow,
                new[] { "\u53d1\u9001\u7533\u8bf7", "\u63d0\u4ea4\u7533\u8bf7", "\u53d1\u9001", "\u63d0\u4ea4" },
                DirectApplyReadyDelayMs);
            var sent = sendButton != null
                ? ClickOrInvoke(sendButton)
                : ClickSendApplyButton(applyWindow);
            if (!sent)
                throw new InvalidOperationException("\u7533\u8bf7\u7a97\u53e3\u5df2\u6253\u5f00\uff0c\u4f46\u65e0\u6cd5\u5b89\u5168\u89e6\u53d1\u201c\u53d1\u9001\u7533\u8bf7\u201d\u3002\u8bf7\u624b\u52a8\u68c0\u67e5\u540e\u53d1\u9001\u3002");
            Log("direct send apply clicked");
        }
    }

    private static List<string> ValidateTargets(List<string> paths)
    {
        if (paths == null || paths.Count == 0)
            throw new ArgumentException("At least one path is required.");

        var validated = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            if (!Path.IsPathRooted(path))
                throw new ArgumentException("Only absolute paths are accepted: " + path);
            if (path.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Windows device paths are not accepted: " + path);

            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                throw new FileNotFoundException("\u9009\u4e2d\u7684\u6587\u4ef6\u6216\u6587\u4ef6\u5939\u4e0d\u5b58\u5728\u3002", fullPath);
            if (!seen.Add(fullPath))
                continue;

            ValidateCommandPath(fullPath);
            validated.Add(fullPath);
        }

        if (validated.Count == 0)
            throw new InvalidOperationException("\u9009\u4e2d\u9879\u91cc\u6ca1\u6709\u53ef\u7528\u7684\u6587\u4ef6\u6216\u6587\u4ef6\u5939\u3002");
        return validated;
    }

    private static void ValidateCommandPath(string path)
    {
        if (Encoding.Default.GetByteCount(path + "\0") > LdCommandDataSize ||
            Encoding.Unicode.GetByteCount(path + "\0") > LdCommandDataSize)
            throw new PathTooLongException("\u8def\u5f84\u8d85\u8fc7\u7eff\u76fe\u672c\u5730\u547d\u4ee4\u7684 512 \u5b57\u8282\u4e0a\u9650\uff1a" + path);
    }

    private static void SendOfficialDecryptSignal(List<string> paths)
    {
        if (!File.Exists(LdMenuPlugPath))
            throw new FileNotFoundException("\u6ca1\u6709\u627e\u5230\u7eff\u76fe\u672c\u5730\u83dc\u5355\u7ec4\u4ef6\u3002", LdMenuPlugPath);

        lock (LdPlugSync)
        {
            EnsureLdMenuPlugInitializedUnsafe();
            if (!LdMenuPlugGetMenuType(10))
                throw new InvalidOperationException("\u5f53\u524d\u7eff\u76fe\u7b56\u7565\u672a\u542f\u7528\u201c\u7533\u8bf7\u89e3\u5bc6\u201d\u83dc\u5355\u3002");

            for (var index = 0; index < paths.Count; index++)
            {
                var isLast = index == paths.Count - 1;
                SendLdCommand(1, Encoding.Default.GetBytes(paths[index] + "\0"), isLast);
                SendLdCommand(0x12, Encoding.Unicode.GetBytes(paths[index] + "\0"), isLast);
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

    private sealed class EncryptionSnapshot
    {
        public int ScannedFiles;
        public readonly List<string> EncryptedFiles = new List<string>();
        public readonly List<string> UnreadableFiles = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }

    private static void StartMcpPipeServer()
    {
        pipeServerStopping = false;
        var thread = new Thread(McpPipeServerLoop)
        {
            IsBackground = true,
            Name = "GreenShield MCP pipe"
        };
        thread.Start();
    }

    private static void McpPipeServerLoop()
    {
        Log("MCP pipe server starting: " + McpPipeName);
        while (!pipeServerStopping)
        {
            NamedPipeServerStream pipe = null;
            try
            {
                pipe = CreateSecurePipe();
                pipe.WaitForConnection();
                var connectedPipe = pipe;
                pipe = null;
                ThreadPool.QueueUserWorkItem(_ => HandlePipeConnection(connectedPipe));
            }
            catch (Exception ex)
            {
                if (!pipeServerStopping)
                    Log("MCP pipe error: " + ex);
            }
            finally
            {
                if (pipe != null) pipe.Dispose();
            }
        }
    }

    private static void HandlePipeConnection(NamedPipeServerStream pipe)
    {
        try
        {
            using (pipe)
            using (var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true))
            using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
            {
                var request = reader.ReadLine();
                if (!string.IsNullOrWhiteSpace(request))
                    writer.WriteLine(HandlePipeRequest(request));
            }
        }
        catch (Exception ex)
        {
            if (!pipeServerStopping)
                Log("MCP client connection error: " + ex.Message);
        }
    }

    private static NamedPipeServerStream CreateSecurePipe()
    {
        var identity = WindowsIdentity.GetCurrent();
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(
            identity.User,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));
        return new NamedPipeServerStream(
            McpPipeName,
            PipeDirection.InOut,
            8,
            PipeTransmissionMode.Byte,
            PipeOptions.None,
            4096,
            4096,
            security);
    }

    private static string HandlePipeRequest(string json)
    {
        var serializer = new JavaScriptSerializer();
        try
        {
            var request = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (request == null)
                throw new ArgumentException("Invalid MCP bridge request.");

            var action = ReadString(request, "action", "").ToLowerInvariant();
            Dictionary<string, object> result;
            switch (action)
            {
                case "status":
                    result = BuildServiceStatus();
                    break;
                case "check":
                    result = BuildEncryptionStatus(ReadStringList(request, "paths"));
                    break;
                case "request":
                    result = ExecuteMcpDecryption(
                        ReadStringList(request, "paths"),
                        ReadBool(request, "wait", true),
                        ClampTimeout(ReadInt(request, "timeoutSeconds", 120)),
                        ReadBool(request, "force", false),
                        ReadString(request, "reason", "Agent requested access to an encrypted file."));
                    break;
                case "wait":
                    result = WaitForTargets(
                        ReadStringList(request, "paths"),
                        ClampTimeout(ReadInt(request, "timeoutSeconds", 120)));
                    break;
                default:
                    throw new ArgumentException("Unknown MCP bridge action: " + action);
            }
            result["ok"] = true;
            result["serviceVersion"] = ServiceVersion;
            return serializer.Serialize(result);
        }
        catch (Exception ex)
        {
            Log("MCP request failed: " + ex);
            return serializer.Serialize(new Dictionary<string, object>
            {
                { "ok", false },
                { "serviceVersion", ServiceVersion },
                { "error", ex.Message },
                { "errorType", ex.GetType().Name }
            });
        }
    }

    private static Dictionary<string, object> BuildServiceStatus()
    {
        var pluginExists = File.Exists(LdMenuPlugPath);
        var policyEnabled = false;
        if (pluginExists)
        {
            lock (LdPlugSync)
            {
                EnsureLdMenuPlugInitializedUnsafe();
                policyEnabled = LdMenuPlugGetMenuType(10);
            }
        }
        return new Dictionary<string, object>
        {
            { "status", pluginExists && policyEnabled ? "ready" : "unavailable" },
            { "pluginExists", pluginExists },
            { "policyEnabled", policyEnabled },
            { "pipeName", McpPipeName },
            { "processId", Process.GetCurrentProcess().Id }
        };
    }

    private static Dictionary<string, object> BuildEncryptionStatus(List<string> rawPaths)
    {
        var paths = ValidateTargets(rawPaths);
        var snapshot = InspectEncryption(paths);
        return BuildSnapshotResult(
            GetInspectionStatus(snapshot, "decrypted"),
            false,
            snapshot,
            0);
    }

    private static Dictionary<string, object> ExecuteMcpDecryption(
        List<string> rawPaths,
        bool wait,
        int timeoutSeconds,
        bool force,
        string reason)
    {
        var paths = ValidateTargets(rawPaths);
        var snapshot = InspectEncryption(paths);
        if (snapshot.EncryptedFiles.Count == 0 && !force)
            return BuildSnapshotResult(
                GetInspectionStatus(snapshot, "already_decrypted"),
                false,
                snapshot,
                0);

        var safeReason = (reason ?? "").Replace("\r", " ").Replace("\n", " ");
        if (safeReason.Length > 500) safeReason = safeReason.Substring(0, 500);
        Log("MCP decrypt request: targets=" + paths.Count +
            " encrypted=" + snapshot.EncryptedFiles.Count +
            " force=" + force +
            " reason=" + safeReason);

        RunDirectApprovalForPaths(paths, true);
        if (!wait || snapshot.EncryptedFiles.Count == 0)
            return BuildSnapshotResult(
                snapshot.EncryptedFiles.Count == 0 ? "submitted_unverified" : "submitted",
                true,
                snapshot,
                0);

        return WaitForEncryptedFiles(snapshot.EncryptedFiles, timeoutSeconds, true, snapshot);
    }

    private static Dictionary<string, object> WaitForTargets(List<string> rawPaths, int timeoutSeconds)
    {
        var paths = ValidateTargets(rawPaths);
        var snapshot = InspectEncryption(paths);
        if (snapshot.EncryptedFiles.Count == 0)
            return BuildSnapshotResult(
                GetInspectionStatus(snapshot, "decrypted"),
                false,
                snapshot,
                0);
        return WaitForEncryptedFiles(snapshot.EncryptedFiles, timeoutSeconds, false, snapshot);
    }

    private static string GetInspectionStatus(EncryptionSnapshot snapshot, string clearStatus)
    {
        if (snapshot.EncryptedFiles.Count > 0) return "encrypted";
        if (snapshot.UnreadableFiles.Count > 0) return "unreadable";
        if (snapshot.Warnings.Count > 0) return "unverified";
        return clearStatus;
    }

    private static Dictionary<string, object> WaitForEncryptedFiles(
        List<string> encryptedFiles,
        int timeoutSeconds,
        bool submitted,
        EncryptionSnapshot initialSnapshot)
    {
        var stopwatch = Stopwatch.StartNew();
        EncryptionSnapshot latest = null;
        while (stopwatch.Elapsed.TotalSeconds < timeoutSeconds)
        {
            latest = InspectSpecificFiles(encryptedFiles);
            if (latest.EncryptedFiles.Count == 0 && latest.UnreadableFiles.Count == 0)
            {
                MergeInspectionIssues(latest, initialSnapshot);
                Log("MCP decrypt completed: files=" + encryptedFiles.Count +
                    " elapsedMs=" + stopwatch.ElapsedMilliseconds);
                return BuildSnapshotResult(
                    GetInspectionStatus(latest, "decrypted"),
                    submitted,
                    latest,
                    stopwatch.ElapsedMilliseconds);
            }
            Thread.Sleep(1000);
        }

        latest = InspectSpecificFiles(encryptedFiles);
        MergeInspectionIssues(latest, initialSnapshot);
        Log("MCP decrypt wait timeout: remaining=" + latest.EncryptedFiles.Count +
            " unreadable=" + latest.UnreadableFiles.Count);
        return BuildSnapshotResult("pending", submitted, latest, stopwatch.ElapsedMilliseconds);
    }

    private static void MergeInspectionIssues(EncryptionSnapshot target, EncryptionSnapshot source)
    {
        if (source == null) return;
        foreach (var path in source.UnreadableFiles)
        {
            if (!ContainsPath(target.UnreadableFiles, path))
                target.UnreadableFiles.Add(path);
        }
        foreach (var warning in source.Warnings)
        {
            if (!target.Warnings.Contains(warning))
                target.Warnings.Add(warning);
        }
    }

    private static bool ContainsPath(List<string> paths, string candidate)
    {
        foreach (var path in paths)
        {
            if (string.Equals(path, candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static EncryptionSnapshot InspectEncryption(List<string> paths)
    {
        var snapshot = new EncryptionSnapshot();
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                InspectFile(path, snapshot);
                continue;
            }
            InspectDirectory(path, snapshot);
        }
        return snapshot;
    }

    private static EncryptionSnapshot InspectSpecificFiles(List<string> paths)
    {
        var snapshot = new EncryptionSnapshot();
        foreach (var path in paths)
            InspectFile(path, snapshot);
        return snapshot;
    }

    private static void InspectDirectory(string root, EncryptionSnapshot snapshot)
    {
        var directories = new Stack<string>();
        directories.Push(root);
        while (directories.Count > 0)
        {
            var directory = directories.Pop();
            try
            {
                foreach (var file in Directory.GetFiles(directory))
                    InspectFile(file, snapshot);
            }
            catch (Exception ex)
            {
                snapshot.Warnings.Add(directory + ": " + ex.Message);
            }

            try
            {
                foreach (var child in Directory.GetDirectories(directory))
                {
                    try
                    {
                        if ((new DirectoryInfo(child).Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            snapshot.Warnings.Add("Skipped reparse point: " + child);
                            continue;
                        }
                        directories.Push(child);
                    }
                    catch (Exception ex)
                    {
                        snapshot.Warnings.Add(child + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                snapshot.Warnings.Add(directory + ": " + ex.Message);
            }
        }
    }

    private static void InspectFile(string path, EncryptionSnapshot snapshot)
    {
        snapshot.ScannedFiles++;
        if (snapshot.ScannedFiles > MaxTrackedFiles)
            throw new InvalidOperationException("The request contains more than " + MaxTrackedFiles + " files. Select a smaller scope.");

        if (!File.Exists(path))
        {
            snapshot.UnreadableFiles.Add(path);
            return;
        }

        try
        {
            var header = new byte[8];
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Read(header, 0, header.Length) == header.Length && IsGreenShieldHeader(header))
                    snapshot.EncryptedFiles.Add(path);
            }
        }
        catch
        {
            snapshot.UnreadableFiles.Add(path);
        }
    }

    private static bool IsGreenShieldHeader(byte[] header)
    {
        return header != null && header.Length >= 8 &&
            header[0] == 0x87 && header[1] == 0x7D && header[2] == 0x1C &&
            header[4] == 0x90 && header[5] == 0x10 && header[6] == 0x29 && header[7] == 0x01;
    }

    private static Dictionary<string, object> BuildSnapshotResult(
        string status,
        bool submitted,
        EncryptionSnapshot snapshot,
        long elapsedMilliseconds)
    {
        return new Dictionary<string, object>
        {
            { "status", status },
            { "submitted", submitted },
            { "scannedFiles", snapshot.ScannedFiles },
            { "encryptedRemaining", snapshot.EncryptedFiles.Count },
            { "unreadableFiles", snapshot.UnreadableFiles.Count },
            { "encryptedSample", TakeSample(snapshot.EncryptedFiles, 20) },
            { "unreadableSample", TakeSample(snapshot.UnreadableFiles, 20) },
            { "warnings", TakeSample(snapshot.Warnings, 20) },
            { "elapsedMilliseconds", elapsedMilliseconds }
        };
    }

    private static string[] TakeSample(List<string> values, int maximum)
    {
        var count = Math.Min(values.Count, maximum);
        var sample = new string[count];
        for (var index = 0; index < count; index++) sample[index] = values[index];
        return sample;
    }

    private static int ClampTimeout(int timeoutSeconds)
    {
        return Math.Max(1, Math.Min(600, timeoutSeconds));
    }

    private static string ReadString(Dictionary<string, object> values, string name, string defaultValue)
    {
        object value;
        return values.TryGetValue(name, out value) && value != null ? Convert.ToString(value) : defaultValue;
    }

    private static int ReadInt(Dictionary<string, object> values, string name, int defaultValue)
    {
        object value;
        int parsed;
        return values.TryGetValue(name, out value) && value != null && int.TryParse(Convert.ToString(value), out parsed)
            ? parsed
            : defaultValue;
    }

    private static bool ReadBool(Dictionary<string, object> values, string name, bool defaultValue)
    {
        object value;
        bool parsed;
        return values.TryGetValue(name, out value) && value != null && bool.TryParse(Convert.ToString(value), out parsed)
            ? parsed
            : defaultValue;
    }

    private static List<string> ReadStringList(Dictionary<string, object> values, string name)
    {
        object value;
        if (!values.TryGetValue(name, out value) || value == null)
            throw new ArgumentException("paths is required.");

        var result = new List<string>();
        var enumerable = value as IEnumerable;
        if (enumerable == null || value is string)
            throw new ArgumentException("paths must be an array of absolute paths.");
        foreach (var item in enumerable)
        {
            if (item != null) result.Add(Convert.ToString(item));
        }
        return result;
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
                    Console.WriteLine("Sending the official local decrypt-application command for selected items.");
                    var onceWindow = args.Length > 1 ? ParseWindowHandle(args[1]) : GetForegroundWindow();
                    RunDirectApprovalFlow(onceWindow, false, true);
                    return 0;

                case "--prepare-once":
                    Console.WriteLine("Opening the official application window without submitting it.");
                    var prepareWindow = args.Length > 1 ? ParseWindowHandle(args[1]) : GetForegroundWindow();
                    RunDirectApprovalFlow(prepareWindow, args.Length == 1, false);
                    return 0;

                case "--list-selected":
                    var requestedWindow = args.Length > 1 ? ParseWindowHandle(args[1]) : GetForegroundWindow();
                    var selectedTarget = FindSelectedExplorerTarget(requestedWindow);
                    if (selectedTarget == null && args.Length == 1) selectedTarget = FindMostRecentSelectedExplorerTarget();
                    if (selectedTarget == null || selectedTarget.Paths == null || selectedTarget.Paths.Count == 0)
                        throw new InvalidOperationException("No selected Explorer files or folders were found.");
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
        Console.WriteLine("  LdDecryptHotkeyCli.exe --once [HWND]       Run the direct local application flow");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --prepare-once [HWND]  Open the application window without submitting");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --list-selected [HWND]  Inspect selected paths without copying");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --probe-direct      Check the local Green Shield interface");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --install-startup   Enable startup");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --uninstall-startup Disable startup");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --status            Show startup status and log path");
        Console.WriteLine("  LdDecryptHotkeyCli.exe --help              Show help");
        Console.WriteLine();
        Console.WriteLine("For normal daily use, select files or folders in Explorer and press F8.");
    }

    private static IntPtr ParseWindowHandle(string value)
    {
        long handle;
        if (!long.TryParse(value, out handle) || handle <= 0)
            throw new ArgumentException("HWND must be a positive decimal integer.");
        return new IntPtr(handle);
    }

    private static ExplorerTarget FindSelectedExplorerTarget(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsExplorerWindow(hwnd))
            return null;

        try
        {
            var shellPaths = GetExplorerSelectedPaths(hwnd);
            if (shellPaths.Count > 0)
            {
                var pathNames = new List<string>();
                foreach (var path in shellPaths) pathNames.Add(Path.GetFileName(path));
                return new ExplorerTarget
                {
                    Hwnd = hwnd,
                    Name = BuildSelectionSummary(pathNames),
                    Paths = shellPaths
                };
            }

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
                    Name = BuildSelectionSummary(selectedNames),
                    Paths = GetDesktopSelectedPaths(hwnd, selectedNames)
                };
            }
        }
        catch (Exception ex)
        {
            Log("target detection error: " + ex.Message);
        }
        return null;
    }

    private static List<string> GetDesktopSelectedPaths(IntPtr hwnd, List<string> selectedNames)
    {
        var paths = new List<string>();

        var className = GetWindowClass(hwnd);
        if (className != "Progman" && className != "WorkerW")
            return paths;

        var desktopRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };
        foreach (var name in selectedNames)
        {
            var candidates = new List<string>();
            foreach (var desktopRoot in desktopRoots)
            {
                if (string.IsNullOrWhiteSpace(desktopRoot) || !Directory.Exists(desktopRoot))
                    continue;

                var exactPath = Path.Combine(desktopRoot, name);
                if (File.Exists(exactPath) || Directory.Exists(exactPath))
                {
                    AddUniquePath(candidates, exactPath);
                    continue;
                }

                foreach (var entry in Directory.GetFileSystemEntries(desktopRoot))
                {
                    if (string.Equals(Path.GetFileNameWithoutExtension(entry), name, StringComparison.OrdinalIgnoreCase))
                        AddUniquePath(candidates, entry);
                }
            }

            if (candidates.Count != 1)
                throw new InvalidOperationException("\u65e0\u6cd5\u552f\u4e00\u786e\u5b9a\u684c\u9762\u9009\u4e2d\u9879\u7684\u5b8c\u6574\u8def\u5f84\uff1a" + name);
            paths.Add(candidates[0]);
        }
        return paths;
    }

    private static void AddUniquePath(List<string> paths, string path)
    {
        foreach (var existing in paths)
        {
            if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                return;
        }
        paths.Add(path);
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
                    if (string.Equals(name, Normalize(needle), StringComparison.OrdinalIgnoreCase))
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
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
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

    private static IntPtr WaitForApprovalWindow(int timeoutMs)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            var windows = FindApprovalWindows();
            if (windows.Count > 0) return windows[0];
            Thread.Sleep(120);
        }
        return IntPtr.Zero;
    }

    private static List<IntPtr> FindApprovalWindows()
    {
        var results = new List<IntPtr>();
        var titles = new[] { "\u65b0\u5efa\u7533\u8bf7", "\u6587\u4ef6\u89e3\u5bc6\u7533\u8bf7" };
        EnumWindows(delegate (IntPtr hwnd, IntPtr lParam)
        {
            if (!IsWindowVisible(hwnd) || !IsApprovalProcess(hwnd))
                return true;

            var title = Normalize(GetTitle(hwnd));
            foreach (var needle in titles)
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

    private static bool IsApprovalProcess(IntPtr hwnd)
    {
        uint processId;
        if (GetWindowThreadProcessId(hwnd, out processId) == 0 || processId == 0)
            return false;

        try
        {
            using (var process = Process.GetProcessById((int)processId))
                return process.ProcessName.StartsWith("LdApproval", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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

    private static bool ClickSendApplyButton(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || !IsApprovalProcess(hwnd))
            return false;

        Rect rect;
        if (!GetWindowRect(hwnd, out rect))
            return false;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            return false;

        // The Green Shield application uses a fixed bottom-right button layout.
        var x = rect.Right - 70;
        var y = rect.Bottom - 46;
        SetForegroundWindow(hwnd);
        Thread.Sleep(150);
        if (!SetCursorPos(x, y))
            return false;
        mouse_event(MouseeventfLeftdown, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MouseeventfLeftup, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(350);
        return true;
    }

    private static bool ClickOrInvoke(AutomationElement element)
    {
        object pattern;
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
        {
            try
            {
                ((InvokePattern)pattern).Invoke();
                Thread.Sleep(350);
                return true;
            }
            catch (Exception ex)
            {
                Log("send button invoke error: " + ex.Message);
            }
        }

        var rect = element.Current.BoundingRectangle;
        if (!rect.IsEmpty)
        {
            if (!SetCursorPos((int)(rect.Left + rect.Width / 2), (int)(rect.Top + rect.Height / 2)))
                return false;
            Thread.Sleep(80);
            mouse_event(MouseeventfLeftdown, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MouseeventfLeftup, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(350);
            return true;
        }
        return false;
    }

    private static void InstallStartup(bool showMessage)
    {
        try
        {
            var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupShortcutName);
            var trayExecutablePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LdDecryptHotkey.exe");
            if (!File.Exists(trayExecutablePath))
                throw new FileNotFoundException("\u6ca1\u6709\u627e\u5230\u6258\u76d8\u7248\u4e3b\u7a0b\u5e8f\u3002", trayExecutablePath);
            CreateStartupShortcut(shortcutPath, trayExecutablePath);
            if (!File.Exists(shortcutPath))
                throw new IOException("\u5f00\u673a\u81ea\u542f\u5feb\u6377\u65b9\u5f0f\u672a\u80fd\u521b\u5efa\u3002");
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

    private static void CreateStartupShortcut(string shortcutPath, string targetPath)
    {
        object shell = null;
        object shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                throw new InvalidOperationException("WScript.Shell is unavailable.");

            shell = Activator.CreateInstance(shellType);
            shortcut = shell.GetType().InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { shortcutPath });
            shortcut.GetType().InvokeMember(
                "TargetPath",
                System.Reflection.BindingFlags.SetProperty,
                null,
                shortcut,
                new object[] { targetPath });
            shortcut.GetType().InvokeMember(
                "WorkingDirectory",
                System.Reflection.BindingFlags.SetProperty,
                null,
                shortcut,
                new object[] { AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') });
            shortcut.GetType().InvokeMember(
                "Save",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shortcut,
                null);
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
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
            lock (LogSync)
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + message + Environment.NewLine);
        }
        catch { }
    }
}
