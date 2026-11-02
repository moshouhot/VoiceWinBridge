using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// 从桥接进程调用 RPC，目标必须是当前前台的独立测试窗口。
internal static class VoiceRpcAction
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    private static DoubaoVoiceClient client;
    private static readonly object LogLock = new object();
    [STAThread]
    private static void Main(string[] args)
    {
        IntPtr target = new IntPtr(Int64.Parse(args[0]));
        string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "action.log");
        File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss.fff") + " caller-before " + VoiceRpcProbe.ReadActiveProfile() + Environment.NewLine);
        client = new DoubaoVoiceClient(delegate(string line) {
            lock (LogLock) File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine);
        }, args[1] == "failure" ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "missing-rpc") : @"C:\Program Files\DoubaoIME\versions\v0.9.3.20");
        int ticks = 0;
        int rounds = 0;
        var timer = new Timer { Interval = 500 };
        timer.Tick += delegate {
            ticks++;
            if (ticks == 2)
            {
                if (GetForegroundWindow() != target) { client.Dispose(); Application.Exit(); return; }
                File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss.fff") + " begin accepted=" + client.Begin(target) + Environment.NewLine);
            }
            if (ticks == 4) client.ShowWave();
            if (ticks == 5) File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss.fff") + " caller-during " + VoiceRpcProbe.ReadActiveProfile() + Environment.NewLine);
            if (ticks == 6)
            {
                string fresh = null;
                var reader = new System.Threading.Thread(delegate() { fresh = VoiceRpcProbe.ReadActiveProfile(); });
                reader.SetApartmentState(System.Threading.ApartmentState.STA);
                reader.Start(); reader.Join();
                File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss.fff") + " fresh-before-stop " + fresh + Environment.NewLine);
                if (args[1] == "shutdown") { timer.Stop(); client.Dispose(); Application.Exit(); return; }
                if (args[1] == "cancel") client.CancelVoice(); else client.StopAndCommit();
            }
            if (ticks == 12)
            {
                if (args[1] == "repeat" && rounds++ == 0) { ticks = 0; return; }
                timer.Stop(); client.Dispose(); Application.Exit();
            }
        };
        timer.Start();
        Application.Run();
    }
}
