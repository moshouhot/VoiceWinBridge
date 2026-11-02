using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal sealed class DoubaoVoiceClient : IDisposable
{
    private const string Dll = "rpc.dll";
    private const string Pipe = @"\\.\pipe\ObricIme\oime-server";
    private const string VoiceWindowClass = "OimeVoiceWaveWindow";
    private const int Start = 1007, Stop = 1008;
    private const int Discard = 1011, Show = 1012, Cancel = 1013;
    private const int FinalizationWatchdogSamples = 15; // 松手后 15 × 100ms = 1.5s
    private readonly Action<string> log;
    private readonly Action<string, IntPtr> preview;
    private readonly string rpcLibrary;
    private readonly object stateLock = new object();
    private readonly Queue<Action> jobs = new Queue<Action>();
    private readonly AutoResetEvent ready = new AutoResetEvent(false);
    private readonly Thread worker;
    private readonly SinkWindow sink;
    private readonly InputMethodRestore inputMethodRestore;
    private readonly List<VoiceEditMarker> voiceEdits = new List<VoiceEditMarker>();
    private volatile bool disposed;
    private bool registered, startPending, started, finishing;
    private int previewGeneration;
    private long nextVoiceEditSequence;
    private string currentComposition = String.Empty;
    private IntPtr target;

    public DoubaoVoiceClient(Action<string> logger, string installDirectory,
        Action<string, IntPtr> previewCallback = null)
    {
        log = logger;
        preview = previewCallback;
        sink = new SinkWindow(log);
        inputMethodRestore = new InputMethodRestore(log);
        rpcLibrary = Path.Combine(installDirectory ?? String.Empty, Dll);
        log("voice startup mode=start-only; ime-restore=session-guarded; snapshot=fresh-sta");
        SetDllDirectory(installDirectory);
        worker = new Thread(Work) { IsBackground = true, Name = "DoubaoVoiceRpc" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        Queue(Register);
    }

    public bool CanBegin
    {
        get
        {
            lock (stateLock)
            {
                if (disposed || finishing || startPending || started)
                    return false;
            }
            // RPC 已返回结果时，豆包的“优化识别中”浮窗可能仍未完全消失。
            // 浮窗仍可见就继续排队，避免过早启动的新会话被豆包忽略。
            return !IsVoiceWindowVisible();
        }
    }

    public bool Begin(IntPtr hwnd) {
        if (IsVoiceWindowVisible())
        {
            log("begin ignored: voice window still visible");
            return false;
        }
        IntPtr focusedWindow = GetFocusedWindow(hwnd);
        lock (stateLock)
        {
            if (disposed || finishing || startPending || started)
            {
                log("begin ignored: busy");
                return false;
            }
            startPending = true;
            target = focusedWindow;
            currentComposition = String.Empty;
            voiceEdits.Clear();
            nextVoiceEditSequence = 0;
        }
        Queue(delegate {
            inputMethodRestore.Capture(hwnd);
            Register();
            // 仅启动语音；不主动接管输入焦点或激活豆包输入法。
            int result = RpcPipe_SimpleMessage(Pipe, Start, 0, 0);
            int generation;
            lock (stateLock)
            {
                started = result == 0;
                startPending = false;
                generation = result == 0 ? ++previewGeneration : previewGeneration;
            }
            Check(result, "start");
            if (result == 0 && preview != null)
                ThreadPool.QueueUserWorkItem(delegate { PollComposition(generation); });
        });
        return true;
    }
    public void ShowWave()
    {
        lock (stateLock) { if (finishing) return; }
        Queue(delegate {
            lock (stateLock) { if (!started || finishing) return; }
            Check(RpcPipe_SimpleMessage(Pipe, Show, 1, 0), "show");
        });
    }
    public void BeginQuotedRange() { AddVoiceEdit("“", "quote-open"); }
    public void EndQuotedRange() { AddVoiceEdit("”", "quote-close"); }
    public void InsertLineBreak() { AddVoiceEdit("\n", "line-break"); }
    public void InsertQuestionMark() { AddVoiceEdit("？", "question"); }

    private void AddVoiceEdit(string value, string name)
    {
        int position;
        lock (stateLock)
        {
            if (!started || finishing)
                return;
            position = currentComposition.Length;
            voiceEdits.Add(new VoiceEditMarker {
                Position = position,
                Sequence = nextVoiceEditSequence++,
                Value = value
            });
        }
        log("voice edit " + name + " position=" + position);
    }

    public void CancelVoice()
    {
        lock (stateLock)
        {
            if (finishing) { log("cancel ignored: finalizing"); return; }
            if (!startPending && !started) return;
            finishing = true;
        }
        Queue(delegate {
            try
            {
                bool active;
                lock (stateLock) { active = started; started = false; }
                if (active) Check(RpcPipe_SimpleMessage(Pipe, Cancel, 0, 0), "cancel");
            }
            finally { CompleteSession("cancel"); }
        });
    }
    public void StopAndCommit()
    {
        lock (stateLock)
        {
            if (finishing) { log("stop ignored: finalizing"); return; }
            finishing = true;
        }
        Queue(delegate {
            try
            {
                bool active;
                lock (stateLock) { active = started; started = false; }
                if (!active) return;
                Check(RpcPipe_SimpleMessage(Pipe, Stop, 0, 0), "stop");
                // 收尾和归还也在同一工作线程完成，下一会话不能抢走旧快照。
                CollectCommit();
            }
            catch { CancelForRecovery(); throw; }
            finally { CompleteSession("stop"); }
        });
    }

    private void Register()
    {
        if (registered) return;
        if (!File.Exists(rpcLibrary))
            throw new FileNotFoundException("Doubao rpc.dll not found", rpcLibrary);
        RpcPipe_RegisterTsfNotifySink(Pipe, sink.Handle, 1);
        registered = true;
        log("RPC sink=0x" + sink.Handle.ToString("X"));
        int cancelResult = RpcPipe_SimpleMessage(Pipe, Cancel, 0, 0);
        int discardResult = RpcPipe_SimpleMessage(Pipe, Discard, 0, 0);
        log("startup recovery cancel=0x" + cancelResult.ToString("X8")
            + " discard=0x" + discardResult.ToString("X8"));
    }

    private void PollComposition(int generation)
    {
        byte[] data = new byte[32768];
        string previous = null;
        int samples = 0;
        while (!disposed)
        {
            bool active;
            IntPtr currentTarget;
            lock (stateLock)
            {
                active = started && generation == previewGeneration;
                currentTarget = target;
            }
            if (!active) break;

            try
            {
                int cursor;
                int length = RpcPipe_GetCompTextUtf8(Pipe, data, data.Length, out cursor);
                if (samples++ % 10 == 0)
                    log("composition sample length=" + length + " cursor=" + cursor);
                string text = length > 0 ? Encoding.UTF8.GetString(data, 0, length) : String.Empty;
                lock (stateLock)
                {
                    if (generation == previewGeneration)
                        currentComposition = text;
                }
                if (!String.Equals(text, previous, StringComparison.Ordinal))
                {
                    previous = text;
                    preview(text, currentTarget);
                }
            }
            catch (Exception ex)
            {
                log("composition preview disabled: " + ex.Message);
                break;
            }
            Thread.Sleep(50);
        }
        bool ownsPreview;
        lock (stateLock) { ownsPreview = generation == previewGeneration; }
        if (preview != null && ownsPreview) preview(String.Empty, IntPtr.Zero);
    }

    private void CollectCommit()
    {
        byte[] data = new byte[32768];
        int lastState = Int32.MinValue;
        int idleSamples = 0;
        for (int i = 0; i < 600 && !disposed; i++)
        {
            ulong session;
            int length = RpcPipe_PeekVoiceCommitUtf8(Pipe, out session, data, data.Length);
            if (length > 0)
            {
                string text = Encoding.UTF8.GetString(data, 0, length);
                int originalCharacters = text.Length;
                text = ApplyVoiceEdits(text);
                bool inserted = Insert(target, text);
                int ack = RpcPipe_AckVoiceCommit(Pipe, session, 1);
                log("commit session=" + session + " bytes=" + length
                    + " chars=" + originalCharacters + " editedChars=" + text.Length
                    + " inserted=" + inserted + " ack=" + ack);
                return;
            }
            int state = RpcPipe_SimpleMessage(Pipe, 1006, 0, 0);
            if (state != lastState)
            {
                log("finalizing state=0x" + state.ToString("X"));
                lastState = state;
            }
            // 旧实现每秒采样一次且要求 3 次空闲，导致语音框消失后仍固定冷却约 2~3 秒。
            // RPC 已空闲且语音框已隐藏时，连续两次 100ms 采样即可安全结束旧会话。
            idleSamples = state == 0 && !IsVoiceWindowVisible() ? idleSamples + 1 : 0;
            if (idleSamples >= 2)
            {
                int discarded = RpcPipe_SimpleMessage(Pipe, Discard, 0, 0);
                log("finalized without commit; discard=0x" + discarded.ToString("X8"));
                return;
            }
            if (i >= FinalizationWatchdogSamples && IsVoiceWindowVisible())
            {
                RecoverFromFinalizationStall(state);
                return;
            }
            Thread.Sleep(100);
        }
        int timeoutDiscarded = RpcPipe_SimpleMessage(Pipe, Discard, 0, 0);
        log("commit timeout; discard=0x" + timeoutDiscarded.ToString("X8"));
    }

    private void RecoverFromFinalizationStall(int state)
    {
        string fallback;
        IntPtr fallbackTarget;
        lock (stateLock)
        {
            // 必须先保存本地实时文本；Cancel/Discard 之后 RPC 侧数据可能立即消失。
            fallback = currentComposition;
            fallbackTarget = target;
        }
        if (!String.IsNullOrEmpty(fallback))
            fallback = ApplyVoiceEdits(fallback);

        // 本轮循环开头已经最后 Peek 过一次 commit；仍为空才进入这里。
        int cancelled = RpcPipe_SimpleMessage(Pipe, Cancel, 0, 0);
        int discarded = RpcPipe_SimpleMessage(Pipe, Discard, 0, 0);
        bool inserted = !String.IsNullOrEmpty(fallback) && Insert(fallbackTarget, fallback);
        log("finalization watchdog state=0x" + state.ToString("X")
            + " fallbackChars=" + (fallback == null ? 0 : fallback.Length)
            + " inserted=" + inserted + " cancel=0x" + cancelled.ToString("X8")
            + " discard=0x" + discarded.ToString("X8"));
    }

    private string ApplyVoiceEdits(string text)
    {
        List<VoiceEditMarker> markers;
        lock (stateLock)
            markers = new List<VoiceEditMarker>(voiceEdits);
        if (markers.Count == 0)
            return text;

        markers.Sort(delegate(VoiceEditMarker left, VoiceEditMarker right) {
            int byPosition = left.Position.CompareTo(right.Position);
            return byPosition != 0 ? byPosition : left.Sequence.CompareTo(right.Sequence);
        });

        var result = new StringBuilder(text);
        for (int end = markers.Count - 1; end >= 0; )
        {
            int start = end;
            int originalPosition = markers[end].Position;
            while (start > 0 && markers[start - 1].Position == originalPosition)
                start--;

            int position = Math.Max(0, Math.Min(originalPosition, result.Length));
            var insertion = new StringBuilder();
            bool containsQuestion = false;
            for (int i = start; i <= end; i++)
            {
                insertion.Append(markers[i].Value);
                containsQuestion |= markers[i].Value == "？";
            }

            if (containsQuestion)
            {
                if (position < result.Length && IsSentencePunctuation(result[position]))
                    result.Remove(position, 1);
                else if (position == result.Length && position > 0
                    && IsSentencePunctuation(result[position - 1]))
                {
                    result.Remove(position - 1, 1);
                    position--;
                }
            }
            result.Insert(position, insertion.ToString());
            end = start - 1;
        }
        log("voice edits applied count=" + markers.Count);
        return result.ToString();
    }

    private static bool IsSentencePunctuation(char value)
    {
        return value == '，' || value == '。' || value == '！' || value == '？'
            || value == ',' || value == '.' || value == '!' || value == '?';
    }

    private void ClearVoiceEditsLocked()
    {
        currentComposition = String.Empty;
        voiceEdits.Clear();
        nextVoiceEditSequence = 0;
    }

    private void Check(int result, string name)
    {
        log(name + " rpc=0x" + result.ToString("X8"));
        if (result != 0)
            throw new InvalidOperationException(name + " RPC failed with " + result);
    }

    private void Queue(Action action)
    {
        lock (jobs) { if (disposed) return; jobs.Enqueue(action); }
        ready.Set();
    }

    private void Work()
    {
        try
        {
            while (!disposed)
            {
                Action action = null;
                lock (jobs) { if (jobs.Count > 0) action = jobs.Dequeue(); }
                if (action == null) { ready.WaitOne(); continue; }
                try { action(); }
                catch (Exception ex)
                {
                    log("RPC error: " + ex.Message);
                    CancelForRecovery();
                    CompleteSession("rpc-error");
                }
            }
        }
        finally
        {
            CancelForRecovery();
            CompleteSession("shutdown");
            if (registered) try { RpcPipe_UnregisterTsfNotifySink(Pipe, sink.Handle, 1); } catch { }
        }
    }

    private void CancelForRecovery()
    {
        if (!registered || !inputMethodRestore.HasSnapshot) return;
        try { Check(RpcPipe_SimpleMessage(Pipe, Cancel, 0, 0), "recovery cancel"); }
        catch (Exception ex) { log("recovery cancel failed: " + ex.Message); }
    }

    private void CompleteSession(string reason)
    {
        lock (stateLock) { finishing = true; started = false; startPending = false; previewGeneration++; }
        if (preview != null) preview(String.Empty, IntPtr.Zero);
        // 豆包关闭浮窗是异步的；先等它收尾，避免归还之后又被旧会话切走。
        if (inputMethodRestore.HasSnapshot)
        {
            for (int i = 0; i < 30 && IsVoiceWindowVisible(); i++) Thread.Sleep(50);
            inputMethodRestore.Restore(reason);
        }
        lock (stateLock) { ClearVoiceEditsLocked(); finishing = false; }
    }

    private bool Insert(IntPtr hwnd, string text)
    {
        if (String.IsNullOrEmpty(text)) return false;
        var inputs = new List<Input>(text.Length * 2);
        foreach (char ch in text) { inputs.Add(MakeInput(ch, false)); inputs.Add(MakeInput(ch, true)); }
        uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(Input)));
        if (sent == inputs.Count) return true;

        int error = Marshal.GetLastWin32Error();
        bool posted = hwnd != IntPtr.Zero && IsWindow(hwnd);
        if (posted)
        {
            foreach (char ch in text)
                posted = PostMessage(hwnd, 0x0102, new IntPtr(ch), IntPtr.Zero) && posted;
        }
        log("SendInput sent=" + sent + "/" + inputs.Count + " error=" + error
            + " target=0x" + hwnd.ToString("X") + " WM_CHAR=" + posted);
        return posted;
    }

    private static bool IsVoiceWindowVisible()
    {
        bool visible = false;
        EnumWindows(delegate(IntPtr window, IntPtr unused)
        {
            if (!IsWindowVisible(window))
                return true;
            var className = new StringBuilder(64);
            if (GetClassName(window, className, className.Capacity) > 0
                && String.Equals(className.ToString(), VoiceWindowClass,
                    StringComparison.Ordinal))
            {
                visible = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return visible;
    }

    private static IntPtr GetFocusedWindow(IntPtr foreground)
    {
        uint processId;
        uint threadId = GetWindowThreadProcessId(foreground, out processId);
        var info = new GuiThreadInfo { cbSize = Marshal.SizeOf(typeof(GuiThreadInfo)) };
        return threadId != 0 && GetGUIThreadInfo(threadId, ref info) && info.hwndFocus != IntPtr.Zero
            ? info.hwndFocus : foreground;
    }

    private static Input MakeInput(char ch, bool up)
    {
        return new Input { type = 1, union = new InputUnion { keyboard = new KeybdInput {
            wScan = ch, dwFlags = 4u | (up ? 2u : 0u), dwExtraInfo = new UIntPtr(0xD06BA07154UL)
        }}};
    }

    public void Dispose()
    {
        disposed = true; ready.Set();
        if (worker.Join(3000)) sink.DestroyHandle();
        else log("shutdown: RPC worker did not finish; input method restore may be incomplete");
    }

    private sealed class SinkWindow : NativeWindow
    {
        private readonly Action<string> log;
        public SinkWindow(Action<string> logger)
        {
            log = logger;
            CreateHandle(new CreateParams { Caption = "DoubaoVoiceBridgeSink" });
        }
        protected override void WndProc(ref Message message)
        {
            log("TSF sink message=0x" + message.Msg.ToString("X")
                + " w=0x" + message.WParam.ToInt64().ToString("X")
                + " l=0x" + message.LParam.ToInt64().ToString("X"));
            base.WndProc(ref message);
        }
    }
    private sealed class VoiceEditMarker
    {
        public int Position;
        public long Sequence;
        public string Value;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint type; public InputUnion union; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct InputUnion {
        [FieldOffset(0)] public KeybdInput keyboard;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KeybdInput {
        public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr dwExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo {
        public int cbSize; public uint flags; public IntPtr hwndActive, hwndFocus, hwndCapture;
        public IntPtr hwndMenuOwner, hwndMoveSize, hwndCaret;
        public Rect rcCaret;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect {
        public int left, top, right, bottom;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern int RpcPipe_SimpleMessage(string pipe, int message, int a, int b);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern void RpcPipe_RegisterTsfNotifySink(string pipe, IntPtr hwnd, ulong cookie);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern void RpcPipe_UnregisterTsfNotifySink(string pipe, IntPtr hwnd, ulong cookie);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern int RpcPipe_GetCompTextUtf8(string pipe, byte[] data, int size, out int cursor);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern int RpcPipe_PeekVoiceCommitUtf8(string pipe, out ulong session, byte[] data, int size);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern int RpcPipe_AckVoiceCommit(string pipe, ulong session, int success);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maximumCount);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
}
