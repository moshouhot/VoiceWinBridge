using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// 仅在独立测试窗口中观察 TSF profile；不修改默认输入法或其他应用的 profile。
internal sealed class VoiceRpcProbe : Form
{
    private readonly TextBox input = new TextBox { Multiline = true, Left = 16, Top = 80, Width = 650, Height = 180 };
    private readonly Label status = new Label { Left = 16, Top = 275, Width = 650, Height = 120 };
    private readonly Timer sample = new Timer { Interval = 50 };
    private readonly Timer stop = new Timer { Interval = 1800 };
    private readonly string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "probe.log");
    private readonly object logLock = new object();
    private DoubaoVoiceClient client;
    private ITfInputProcessorProfileMgr profiles;
    private string previous;
    private static readonly Guid KeyboardCategory = new Guid("34745c63-b2f0-4784-8b67-5e12c8701a31");

    private void SelectChinese(Guid clsid, Guid profile, string name)
    {
        foreach (InputLanguage language in InputLanguage.InstalledInputLanguages)
            if (language.Culture.Name == "zh-CN")
            {
                InputLanguage.CurrentInputLanguage = language;
                break;
            }
        int result = profiles.ActivateProfile(1, 0x0804, ref clsid, ref profile, IntPtr.Zero, 0);
        Log("select-" + name + " hr=" + result.ToString("X8"));
        if (result != 0) throw new InvalidOperationException("Could not select the test input method.");
        input.Focus();
    }

    private void SelectEnglish()
    {
        foreach (InputLanguage language in InputLanguage.InstalledInputLanguages)
            if (language.Culture.Name == "en-US")
            {
                InputLanguage.CurrentInputLanguage = language;
                Log("selected English layout=" + language.Handle.ToString("X"));
                return;
            }
        throw new InvalidOperationException("The English test layout is not installed.");
    }

    internal static string ReadActiveProfile()
    {
        var manager = (ITfInputProcessorProfileMgr)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("33c53a50-f456-4884-b049-85fd643ecfed")));
        try
        {
            Guid category = KeyboardCategory;
            Profile p;
            int hr = manager.GetActiveProfile(ref category, out p);
            return "hr=" + hr.ToString("X8") + " type=" + p.Type + " lang=0x" + p.Language.ToString("X4")
                + " clsid=" + p.Clsid + " profile=" + p.ProfileGuid + " hkl=0x" + p.Hkl.ToString("X");
        }
        finally { Marshal.ReleaseComObject(manager); }
    }

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.Run(new VoiceRpcProbe());
    }

    private VoiceRpcProbe()
    {
        Text = "Voice RPC restore probe - " + (Environment.GetCommandLineArgs().Length > 1 ? Environment.GetCommandLineArgs()[1] : "manual");
        ClientSize = new Size(690, 400);
        var sogou = new Button { Text = "Select Sogou (test window)", Left = 16, Top = 16, Width = 210 };
        var begin = new Button { Text = "Voice then commit", Left = 238, Top = 16, Width = 150 };
        var cancel = new Button { Text = "Voice then cancel", Left = 400, Top = 16, Width = 150 };
        sogou.Click += delegate {
            Guid clsid = new Guid("e7ea138e-69f8-11d7-a6ea-00065b844310");
            Guid profile = new Guid("e7ea138f-69f8-11d7-a6ea-00065b844311");
            SelectChinese(clsid, profile, "sogou");
        };
        begin.Click += delegate { Start(false); };
        cancel.Click += delegate { Start(true); };
        Controls.AddRange(new Control[] { sogou, begin, cancel, input, status });
        AcceptButton = begin;
        Shown += delegate {
            File.WriteAllText(logPath, String.Empty);
            profiles = (ITfInputProcessorProfileMgr)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("33c53a50-f456-4884-b049-85fd643ecfed")));
            client = new DoubaoVoiceClient(Log, @"C:\Program Files\DoubaoIME\versions\v0.9.3.20", delegate(string text, IntPtr target) { Log("preview-chars=" + text.Length); });
            sample.Start();
            input.Focus();
            if (Environment.GetCommandLineArgs().Length > 1)
            {
                var auto = new Timer { Interval = 1000 };
                auto.Tick += delegate {
                    if (!ContainsFocus) return;
                    auto.Stop(); auto.Dispose();
                    if (Environment.GetCommandLineArgs()[1] == "--external-english") SelectEnglish();
                    else if (Environment.GetCommandLineArgs()[1] == "--external-manual-sogou")
                    {
                        Guid clsid = new Guid("9d2b2e2b-3c93-4d2f-9d35-6eeb85f0d2b0");
                        Guid profile = new Guid("2b4d4b3a-4d4f-4c0a-8e66-7f771a2b9c10");
                        SelectChinese(clsid, profile, "doubao");
                    }
                    else sogou.PerformClick();
                    var start = new Timer { Interval = 500 };
                    start.Tick += delegate {
                        start.Stop(); start.Dispose();
                        if (ContainsFocus && Environment.GetCommandLineArgs()[1].StartsWith("--external", StringComparison.Ordinal))
                        {
                            string mode = Environment.GetCommandLineArgs()[1].Replace("--external-", "");
                            if (mode == "--external") mode = "commit";
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VoiceRpcAction.exe"), Handle.ToInt64() + " " + mode) {
                                    UseShellExecute = false, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                                });
                            if (mode == "manual" || mode == "manual-sogou" || mode == "leave")
                            {
                                var change = new Timer { Interval = 2500 };
                                change.Tick += delegate {
                                    change.Stop(); change.Dispose();
                                    if (mode == "manual")
                                    {
                                        SelectEnglish();
                                    }
                                    else if (mode == "manual-sogou") sogou.PerformClick();
                                    else
                                    {
                                        var other = new Form { Text = "Restore guard: another window", Owner = this };
                                        other.Show(); other.Activate();
                                        Log("foreground changed to another test window");
                                    }
                                };
                                change.Start();
                            }
                        }
                        else if (ContainsFocus) Start(Environment.GetCommandLineArgs()[1] == "--cancel");
                        else Log("auto aborted: test window lost focus");
                    };
                    start.Start();
                };
                auto.Start();
            }
        };
        sample.Tick += delegate {
            Guid category = KeyboardCategory;
            Profile p;
            int hr = profiles.GetActiveProfile(ref category, out p);
            string value = "hr=" + hr.ToString("X8") + " type=" + p.Type + " lang=0x" + p.Language.ToString("X4")
                + " clsid=" + p.Clsid + " profile=" + p.ProfileGuid + " hkl=0x" + p.Hkl.ToString("X");
            status.Text = value + "\r\nText characters: " + input.TextLength;
            if (value != previous) { Log("profile " + value); previous = value; }
        };
        FormClosed += delegate { sample.Stop(); stop.Stop(); if (client != null) client.Dispose(); };
    }

    private void Start(bool cancel)
    {
        input.Focus();
        Log("begin accepted=" + client.Begin(Handle));
        var show = new Timer { Interval = 600 };
        show.Tick += delegate { show.Stop(); show.Dispose(); client.ShowWave(); };
        show.Start();
        stop.Stop();
        EventHandler handler = null;
        handler = delegate {
            stop.Stop(); stop.Tick -= handler;
            if (cancel) client.CancelVoice(); else client.StopAndCommit();
            Log(cancel ? "cancel requested" : "commit requested");
        };
        stop.Tick += handler;
        stop.Start();
    }

    private void Log(string line)
    {
        lock (logLock) File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Profile
    {
        public uint Type;
        public ushort Language;
        public Guid Clsid, ProfileGuid, Category;
        public IntPtr Substitute;
        public uint Caps;
        public IntPtr Hkl;
        public uint Flags;
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
        [PreserveSig] int GetActiveProfile(ref Guid category, out Profile profile);
    }
}
