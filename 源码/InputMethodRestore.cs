using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

// RPC 工作线程持有会话快照；TSF 读取使用独立 STA，避免常驻线程的旧 profile。
// 不修改默认输入法或启用状态。
internal sealed class InputMethodRestore
{
    private readonly Action<string> log;
    private InputMethodProfile original;
    private IntPtr window, layout;
    private uint processId, threadId;
    private bool captured;

    internal InputMethodRestore(Action<string> logger) { log = logger; }
    internal bool HasSnapshot { get { return captured; } }

    internal void Capture(IntPtr target)
    {
        captured = false;
        window = GetAncestor(target, 2); // GA_ROOT，允许同一窗口内的子控件交接。
        if (window == IntPtr.Zero || GetAncestor(GetForegroundWindow(), 2) != window)
            throw new InvalidOperationException("The voice target is no longer in the foreground.");
        threadId = GetWindowThreadProcessId(window, out processId);
        layout = GetKeyboardLayout(threadId);
        original = InputMethodProfiles.ReadFresh();
        if (!IsOriginalForeground())
            throw new InvalidOperationException("The voice target changed while reading its input method.");
        captured = true;
        log("ime captured " + original);
    }

    internal void Restore(string reason)
    {
        if (!captured) return;
        captured = false; // 每次快照只归还一次，不能带到下一轮。
        try
        {
            if (!IsOriginalForeground()) { log("ime restore " + reason + " skipped=foreground-changed"); return; }
            InputMethodProfile current = InputMethodProfiles.ReadFresh();
            log("ime current " + current);
            string decision = Decide(original, current, IsOriginalForeground(), InputMethodProfiles.IsDoubao(current));
            if (decision != "restore") { log("ime restore " + reason + " skipped=" + decision); return; }

            // 不同语言先向原窗口请求原布局；同语言的搜狗/豆包不能靠 HKL 区分。
            if (original.Language != current.Language && layout != IntPtr.Zero)
            {
                if (!PostMessage(window, 0x0050, IntPtr.Zero, layout))
                    throw new InvalidOperationException("The original input language request failed.");
                Thread.Sleep(50);
                current = InputMethodProfiles.ReadFresh();
                decision = Decide(original, current, IsOriginalForeground(), InputMethodProfiles.IsDoubao(current));
                if (decision != "restore") { log("ime restore " + reason + " skipped=" + decision); return; }
            }

            if (!IsOriginalForeground()) { log("ime restore " + reason + " skipped=foreground-changed"); return; }
            int result = InputMethodProfiles.Activate(original, InputMethodProfiles.ForSession | InputMethodProfiles.DontCareLanguage);
            log("ime restore requested reason=" + reason + " hr=0x" + result.ToString("X8") + " " + original);
            if (result != 0) return;
            for (int i = 0; i < 10; i++)
            {
                if (!IsOriginalForeground()) { log("ime restore verification stopped: foreground changed"); return; }
                current = InputMethodProfiles.ReadFresh();
                if (!IsOriginalForeground()) { log("ime restore verification stopped: foreground changed"); return; }
                if (original.SameAs(current)) { log("ime restore profile confirmed reason=" + reason); return; }
                if (!InputMethodProfiles.IsDoubao(current)) { log("ime restore verification stopped: another profile selected"); return; }
                Thread.Sleep(50);
            }
            log("ime restore profile confirmation timed out reason=" + reason);
        }
        catch (Exception ex) { log("ime restore failed reason=" + reason + ": " + ex.Message); }
    }

    internal static string Decide(InputMethodProfile saved, InputMethodProfile current, bool sameWindow, bool currentIsDoubao)
    {
        if (!sameWindow) return "foreground-changed";
        if (saved.SameAs(current)) return "already-original";
        if (!currentIsDoubao) return "another-profile-selected";
        return "restore";
    }

    private bool IsOriginalForeground()
    {
        uint pid;
        return IsWindow(window) && GetWindowThreadProcessId(window, out pid) == threadId
            && pid == processId && GetAncestor(GetForegroundWindow(), 2) == window;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}

// 字段顺序、接口槽位和常量依据 Windows SDK msctf.h。
[StructLayout(LayoutKind.Sequential)]
internal struct InputMethodProfile
{
    public uint Type;
    public ushort Language;
    public Guid Clsid, ProfileGuid, Category;
    public IntPtr Substitute;
    public uint Caps;
    public IntPtr Hkl;
    public uint Flags;

    internal bool SameAs(InputMethodProfile other)
    {
        return Type == other.Type && Language == other.Language
            && (Type == 2 ? Hkl == other.Hkl : Clsid == other.Clsid && ProfileGuid == other.ProfileGuid);
    }
    public override string ToString()
    {
        return "type=" + Type + " lang=0x" + Language.ToString("X4") + " clsid=" + Clsid
            + " profile=" + ProfileGuid + " hkl=0x" + Hkl.ToString("X");
    }
}

internal static class InputMethodProfiles
{
    internal const uint ForSession = 0x20000000;
    internal const uint DontCareLanguage = 0x00000004;
    private static readonly Guid KeyboardCategory = new Guid("34745c63-b2f0-4784-8b67-5e12c8701a31");

    internal static InputMethodProfile ReadFresh()
    {
        InputMethodProfile profile = new InputMethodProfile();
        Exception error = null;
        var reader = new Thread(delegate() {
            try { profile = Read(); }
            catch (Exception ex) { error = ex; }
        }) { IsBackground = true, Name = "InputMethodSnapshot" };
        reader.SetApartmentState(ApartmentState.STA);
        reader.Start();
        reader.Join();
        if (error != null) throw new InvalidOperationException("Input method snapshot failed.", error);
        return profile;
    }

    internal static InputMethodProfile Read()
    {
        ITfInputProcessorProfileMgr manager = Create();
        try
        {
            Guid category = KeyboardCategory;
            InputMethodProfile profile;
            int result = manager.GetActiveProfile(ref category, out profile);
            if (result != 0) throw new InvalidOperationException("GetActiveProfile failed: 0x" + result.ToString("X8"));
            return profile;
        }
        finally { Marshal.ReleaseComObject(manager); }
    }

    internal static int Activate(InputMethodProfile profile, uint flags)
    {
        ITfInputProcessorProfileMgr manager = Create();
        try
        {
            return manager.ActivateProfile(profile.Type, profile.Language, ref profile.Clsid,
                ref profile.ProfileGuid, profile.Type == 2 ? profile.Hkl : IntPtr.Zero, flags);
        }
        finally { Marshal.ReleaseComObject(manager); }
    }

    internal static bool IsDoubao(InputMethodProfile profile)
    {
        if (profile.Type != 1) return false;
        string path = @"SOFTWARE\Microsoft\CTF\TIP\" + profile.Clsid.ToString("B")
            + @"\LanguageProfile\0x" + profile.Language.ToString("x8") + @"\" + profile.ProfileGuid.ToString("B");
        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path))
        {
            string name = key == null ? null : key.GetValue("Description") as string;
            return name != null && (name.IndexOf("豆包", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Doubao", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    private static ITfInputProcessorProfileMgr Create()
    {
        return (ITfInputProcessorProfileMgr)Activator.CreateInstance(Type.GetTypeFromCLSID(
            new Guid("33c53a50-f456-4884-b049-85fd643ecfed")));
    }

    [ComImport, Guid("71c6e74c-0f28-11d8-a82a-00065b84435c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfInputProcessorProfileMgr
    {
        [PreserveSig] int ActivateProfile(uint type, ushort language, ref Guid clsid, ref Guid profile, IntPtr hkl, uint flags);
        [PreserveSig] int DeactivateProfile();
        [PreserveSig] int GetProfile();
        [PreserveSig] int EnumProfiles();
        [PreserveSig] int ReleaseInputProcessor();
        [PreserveSig] int RegisterProfile();
        [PreserveSig] int UnregisterProfile();
        [PreserveSig] int GetActiveProfile(ref Guid category, out InputMethodProfile profile);
    }
}
