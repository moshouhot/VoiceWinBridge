using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class VoiceWinBridge
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkRMenu = 0xA5;
    private const int VkReturn = 0x0D;
    private const int VkShift = 0x10;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkOemQuestion = 0xBF;
    private const int VkOemQuote = 0xDE;
    private const uint InputKeyboard = 1;
    private const uint KeyeventfExtendedKey = 0x0001;
    private const uint KeyeventfKeyup = 0x0002;
    private const ulong InjectionMarker = 0xD06BA07154UL;
    private const int BeginRetryMilliseconds = 25;
    // 语音+按键编辑仍是原型；先关闭入口，稳定最终提交后再继续验证。
    private static readonly bool EnableVoiceEditingPrototype = false;

    private static readonly object StateLock = new object();
    private static readonly object LogLock = new object();
    private static readonly Queue<string> LogQueue = new Queue<string>();
    private static readonly HashSet<int> VoiceCommandKeysDown = new HashSet<int>();
    private static readonly AutoResetEvent LogReady = new AutoResetEvent(false);
    private static readonly LowLevelKeyboardProc HookCallback = KeyboardHook;
    private static IntPtr hookHandle = IntPtr.Zero;
    private static NotifyIcon trayIcon;
    private static VoicePreviewOverlay previewOverlay;
    private static DoubaoVoiceClient voiceClient;
    private static BridgeSettings settings;
    private static System.Threading.Timer holdTimer;
    private static System.Threading.Timer beginRetryTimer;
    private static Thread logThread;
    private static volatile bool logStopping;
    private static bool enabled = true;
    private static bool triggerHeld;
    private static bool triggerCombination;
    private static bool voiceShown;
    private static bool voiceAccepted;
    private static bool voiceQuoteHeld;
    private static int heldTriggerKey;
    private static long triggerDownTimestamp;
    private static int holdGeneration;
    private static IntPtr targetWindow;

    private static readonly string LogPath = System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "debug.log");

    private static void StartLogWriter()
    {
        try { System.IO.File.WriteAllText(LogPath, string.Empty); } catch { }
        logStopping = false;
        logThread = new Thread(LogWorker) { IsBackground = true, Name = "VoiceWinBridgeLog" };
        logThread.Start();
    }

    private static void StopLogWriter()
    {
        logStopping = true;
        LogReady.Set();
        if (logThread != null && Thread.CurrentThread != logThread)
            logThread.Join(1000);
    }

    private static void Log(string message)
    {
        try
        {
            lock (LogLock)
                LogQueue.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + " " + message);
            LogReady.Set();
        }
        catch
        {
            // 日志失败不影响桥接功能。
        }
    }

    private static void LogWorker()
    {
        while (true)
        {
            LogReady.WaitOne();
            string[] lines;
            lock (LogLock)
            {
                lines = LogQueue.ToArray();
                LogQueue.Clear();
            }
            if (lines.Length > 0)
            {
                try
                {
                    System.IO.File.AppendAllText(LogPath,
                        String.Join(Environment.NewLine, lines) + Environment.NewLine);
                }
                catch { }
            }
            if (logStopping)
            {
                lock (LogLock)
                {
                    if (LogQueue.Count == 0)
                        return;
                }
            }
        }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--wait-for")
        {
            int oldProcessId;
            if (Int32.TryParse(args[1], out oldProcessId))
            {
                try { Process.GetProcessById(oldProcessId).WaitForExit(5000); }
                catch { }
            }
        }

        bool createdNew;
        bool experimental = String.Equals(
            System.IO.Path.GetFileNameWithoutExtension(Application.ExecutablePath),
            "VoiceWinBridgePreview", StringComparison.OrdinalIgnoreCase);
        string mutexName = experimental
            ? @"Local\DoubaoVoiceWinBridgePreview"
            : @"Local\DoubaoVoiceWinBridge";
        using (var mutex = new Mutex(true, mutexName, out createdNew))
        {
            if (!createdNew)
                return;

            StartLogWriter();
            Log("启动 VoiceWinBridge v" + AppIdentity.Version);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            settings = BridgeSettings.Load(Log);
            previewOverlay = new VoicePreviewOverlay();
            IntPtr previewHandle = previewOverlay.Handle;
            voiceClient = new DoubaoVoiceClient(Log, settings.InstallDirectory, UpdatePreview);
            Log("Doubao directory=" + settings.InstallDirectory);
            hookHandle = SetHook(HookCallback);
            if (hookHandle == IntPtr.Zero)
            {
                voiceClient.Dispose();
                Log("钩子安装失败");
                MessageBox.Show("无法安装键盘钩子。", "豆包语音 Win 桥接", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Log("钩子已安装");

            var settingsItem = new ToolStripMenuItem("设置...");
            settingsItem.Click += delegate { ShowSettings(); };

            var toggleItem = new ToolStripMenuItem("暂停") { Checked = false };
            toggleItem.Click += delegate
            {
                lock (StateLock)
                {
                    ReleaseSyntheticKeys();
                    enabled = !enabled;
                    toggleItem.Text = enabled ? "暂停" : "启用";
                    toggleItem.Checked = !enabled;
                    trayIcon.Text = enabled ? BuildTrayText() : "豆包语音：已暂停";
                }
            };

            var exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += delegate { Application.Exit(); };

            var menu = new ContextMenuStrip();
            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(toggleItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            trayIcon = new NotifyIcon
            {
                Icon = AppIdentity.Icon,
                Text = BuildTrayText(),
                ContextMenuStrip = menu,
                Visible = true
            };
            trayIcon.DoubleClick += delegate { ShowSettings(); };

            Application.ApplicationExit += delegate
            {
                lock (StateLock)
                    ReleaseSyntheticKeys();
                if (holdTimer != null)
                    holdTimer.Dispose();
                if (beginRetryTimer != null)
                    beginRetryTimer.Dispose();
                voiceClient.Dispose();
                trayIcon.Visible = false;
                UnhookWindowsHookEx(hookHandle);
                StopLogWriter();
            };

            Application.Run();
            GC.KeepAlive(HookCallback);
        }
    }

    private static IntPtr SetHook(LowLevelKeyboardProc callback)
    {
        using (Process process = Process.GetCurrentProcess())
        using (ProcessModule module = process.MainModule)
            return SetWindowsHookEx(WhKeyboardLl, callback, GetModuleHandle(module.ModuleName), 0);
    }

    private static bool IsTriggerKey(int key)
    {
        return (settings.EnableWin && (key == VkLWin || key == VkRWin))
            || (settings.EnableRightAlt && key == VkRMenu);
    }

    private static string BuildTrayText()
    {
        if (settings.EnableWin && settings.EnableRightAlt)
            return "豆包语音：长按 Win / 右 Alt";
        if (settings.EnableRightAlt)
            return "豆包语音：长按右 Alt";
        if (settings.EnableWin)
            return "豆包语音：长按 Win";
        return "豆包语音：未设置触发键";
    }

    private static void UpdatePreview(string text, IntPtr target)
    {
        VoicePreviewOverlay overlay = previewOverlay;
        if (overlay == null || overlay.IsDisposed)
            return;
        try
        {
            if (overlay.InvokeRequired)
                overlay.BeginInvoke(new Action<string, IntPtr>(UpdatePreview), text, target);
            else
                overlay.SetPreview(text, target);
        }
        catch (InvalidOperationException) { }
    }

    private static void ShowSettings()
    {
        lock (StateLock)
            ReleaseSyntheticKeys();

        using (var form = new SettingsForm(settings))
        {
            if (form.ShowDialog() != DialogResult.OK)
                return;
            settings.EnableWin = form.EnableWin;
            settings.EnableRightAlt = form.EnableRightAlt;
            settings.HoldMilliseconds = form.HoldMilliseconds;
            settings.StartWithWindows = form.StartWithWindows;
            bool restartRequired = !String.Equals(settings.InstallDirectory,
                form.InstallDirectory, StringComparison.OrdinalIgnoreCase);
            settings.InstallDirectory = form.InstallDirectory;
            try
            {
                BridgeSettings.SetStartWithWindows(settings.StartWithWindows);
                settings.StartWithWindows = BridgeSettings.IsStartWithWindowsEnabled();
                if (settings.StartWithWindows != form.StartWithWindows)
                    throw new InvalidOperationException("开机自启设置没有成功写入注册表。");
                settings.Save();
                trayIcon.Text = enabled ? BuildTrayText() : "豆包语音：已暂停";
                Log("settings saved win=" + settings.EnableWin + " rightAlt="
                    + settings.EnableRightAlt + " holdMs=" + settings.HoldMilliseconds
                    + " startup=" + settings.StartWithWindows
                    + " directory=" + settings.InstallDirectory);
                if (restartRequired)
                {
                    var startInfo = new ProcessStartInfo(
                        Application.ExecutablePath,
                        "--wait-for " + Process.GetCurrentProcess().Id) {
                        UseShellExecute = false
                    };
                    Process.Start(startInfo);
                    Application.Exit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法保存设置：" + ex.Message, "豆包语音桥接",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private static bool IsVoiceCommandShift(int key)
    {
        return key == VkShift || key == VkLShift || key == VkRShift;
    }

    private static bool IsVoiceCommandKey(int key)
    {
        return IsVoiceCommandShift(key) || key == VkOemQuote
            || key == VkReturn || key == VkOemQuestion;
    }

    private static bool HandleVoiceCommandKey(int key, bool keyDown, bool keyUp)
    {
        bool wasSwallowed = VoiceCommandKeysDown.Contains(key);
        bool commandMode = EnableVoiceEditingPrototype
            && triggerHeld && !triggerCombination && voiceAccepted && voiceShown;

        if (keyUp && wasSwallowed)
        {
            VoiceCommandKeysDown.Remove(key);
            if (key == VkOemQuote && voiceQuoteHeld)
            {
                voiceQuoteHeld = false;
                voiceClient.EndQuotedRange();
            }
            return true;
        }
        if (!commandMode || !keyDown || !IsVoiceCommandKey(key))
            return false;

        bool firstDown = VoiceCommandKeysDown.Add(key);
        if (!firstDown)
            return true;

        if (key == VkOemQuote)
        {
            voiceQuoteHeld = true;
            voiceClient.BeginQuotedRange();
        }
        else if (key == VkReturn)
            voiceClient.InsertLineBreak();
        else if (key == VkOemQuestion)
            voiceClient.InsertQuestionMark();
        // Shift 只作为引号/问号的物理修饰键吞掉，不单独产生编辑命令。
        return true;
    }

    private static void CloseVoiceQuoteIfNeeded()
    {
        if (!voiceQuoteHeld)
            return;
        voiceQuoteHeld = false;
        voiceClient.EndQuotedRange();
    }

    private static IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || !enabled)
            return CallNextHookEx(hookHandle, code, wParam, lParam);

        var data = (KbdLlHookStruct)Marshal.PtrToStructure(lParam, typeof(KbdLlHookStruct));
        if (data.dwExtraInfo.ToUInt64() == InjectionMarker)
            return CallNextHookEx(hookHandle, code, wParam, lParam);

        int message = wParam.ToInt32();
        bool keyDown = message == WmKeyDown || message == WmSysKeyDown;
        bool keyUp = message == WmKeyUp || message == WmSysKeyUp;
        int key = unchecked((int)data.vkCode);

        lock (StateLock)
        {
            if (HandleVoiceCommandKey(key, keyDown, keyUp))
                return new IntPtr(1);

            if (IsTriggerKey(key) && (!triggerHeld || key == heldTriggerKey))
            {
                if (keyDown && !triggerHeld)
                {
                    triggerHeld = true;
                    triggerCombination = false;
                    voiceQuoteHeld = false;
                    heldTriggerKey = key;
                    triggerDownTimestamp = Stopwatch.GetTimestamp();
                    Log("trigger down vk=0x" + key.ToString("X"));
                    targetWindow = GetForegroundWindow();
                    int generation = ++holdGeneration;
                    voiceAccepted = false;
                    // 所有启动检查都放到后台计时器中，避免在低级键盘钩子里枚举窗口或等待 RPC。
                    Log("trigger accepted: waiting for voice client and window");
                    ScheduleBeginRetry(generation);
                }
                else if (keyUp && triggerHeld && key == heldTriggerKey)
                {
                    CompleteTriggerRelease(false);
                }

                return new IntPtr(1);
            }

            if (triggerHeld && keyDown && !triggerCombination)
            {
                Log("组合键 vk=0x" + key.ToString("X") + " -> 还原触发键");
                holdGeneration++;
                StopBeginRetry();
                CloseVoiceQuoteIfNeeded();
                voiceClient.CancelVoice();
                SendKey(heldTriggerKey, false);
                triggerCombination = true;
            }
        }

        return CallNextHookEx(hookHandle, code, wParam, lParam);
    }

    private static long ElapsedMilliseconds(long start)
    {
        return (Stopwatch.GetTimestamp() - start) * 1000L / Stopwatch.Frequency;
    }

    private static void StopBeginRetry()
    {
        if (beginRetryTimer != null)
            beginRetryTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private static void ScheduleBeginRetry(int generation)
    {
        if (beginRetryTimer != null)
            beginRetryTimer.Dispose();
        beginRetryTimer = new System.Threading.Timer(delegate
        {
            lock (StateLock)
            {
                if (!triggerHeld || triggerCombination || generation != holdGeneration)
                    return;
                if (!voiceClient.CanBegin)
                    return;

                if (beginRetryTimer != null)
                    beginRetryTimer.Change(Timeout.Infinite, Timeout.Infinite);
                StartVoiceForHeldTrigger(generation, true);
            }
        }, null, BeginRetryMilliseconds, BeginRetryMilliseconds);
    }

    private static void StartVoiceForHeldTrigger(int generation, bool wasQueued)
    {
        if (!triggerHeld || triggerCombination || generation != holdGeneration)
            return;

        voiceAccepted = voiceClient.Begin(targetWindow);
        if (!voiceAccepted)
        {
            ScheduleBeginRetry(generation);
            return;
        }

        long elapsed = ElapsedMilliseconds(triggerDownTimestamp);
        int remaining = (int)Math.Max(0L, settings.HoldMilliseconds - elapsed);
        Log((wasQueued ? "queued trigger started" : "trigger started")
            + " elapsed=" + elapsed + "ms remaining=" + remaining + "ms");
        if (holdTimer != null)
            holdTimer.Dispose();
        holdTimer = new System.Threading.Timer(delegate
        {
            lock (StateLock)
            {
                if (!triggerHeld || triggerCombination || !voiceAccepted
                    || generation != holdGeneration)
                    return;
                voiceShown = true;
                voiceClient.ShowWave();
                Log("voice show");
            }
        }, null, remaining, Timeout.Infinite);
    }

    private static void CompleteTriggerRelease(bool recovered)
    {
        long elapsed = ElapsedMilliseconds(triggerDownTimestamp);
        Log((recovered ? "trigger release recovered" : "trigger up")
            + " vk=0x" + heldTriggerKey.ToString("X") + " elapsed=" + elapsed
            + "ms combo=" + triggerCombination);
        holdGeneration++;
        StopBeginRetry();
        CloseVoiceQuoteIfNeeded();
        if (triggerCombination)
            SendKey(heldTriggerKey, true);
        else if (voiceAccepted && (voiceShown || elapsed >= settings.HoldMilliseconds))
            voiceClient.StopAndCommit();
        else
        {
            voiceClient.CancelVoice();
            // 只要语音没有真正触发，就恢复被拦截的原始按键；否则一次较慢的 Win 按压会被吞掉。
            SendKeyPress(heldTriggerKey);
        }

        triggerHeld = false;
        triggerCombination = false;
        voiceShown = false;
        voiceAccepted = false;
        heldTriggerKey = 0;
        targetWindow = IntPtr.Zero;
    }

    private static void ReleaseSyntheticKeys()
    {
        if (!triggerHeld)
            return;

        if (triggerCombination)
            SendKey(heldTriggerKey, true);
        voiceQuoteHeld = false;
        voiceClient.CancelVoice();
        holdGeneration++;
        StopBeginRetry();

        triggerHeld = false;
        triggerCombination = false;
        voiceShown = false;
        voiceAccepted = false;
        heldTriggerKey = 0;
    }

    private static void SendKeyPress(int virtualKey)
    {
        SendKey(virtualKey, false);
        SendKey(virtualKey, true);
    }

    private static void SendKey(int virtualKey, bool keyUp)
    {
        Log("注入 vk=0x" + virtualKey.ToString("X") + " " + (keyUp ? "up" : "down"));
        // 右 Alt (0xA5) 与右 Win (0x5C) 属于扩展键；注入时必须带 KEYEVENTF_EXTENDEDKEY，
        // 否则部分输入法/驱动会按左键处理，导致豆包识别不到“右 Alt”长按。
        uint flags = 0;
        if (virtualKey == VkRWin || virtualKey == VkRMenu)
            flags |= KeyeventfExtendedKey;
        if (keyUp)
            flags |= KeyeventfKeyup;

        var input = new Input
        {
            type = InputKeyboard,
            union = new InputUnion
            {
                keyboard = new KeybdInput
                {
                    wVk = unchecked((ushort)virtualKey),
                    dwFlags = flags,
                    dwExtraInfo = new UIntPtr(InjectionMarker)
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input)));
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint type;
        public InputUnion union;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeybdInput keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, Input[] inputs, int sizeOfInput);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

}
