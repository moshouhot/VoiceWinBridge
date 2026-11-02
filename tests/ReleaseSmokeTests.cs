using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;

internal static class ReleaseSmokeTests
{
    private static int checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        checks++;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        string executable = Path.GetFullPath(args[0]);
        var assembly = Assembly.LoadFile(executable);
        Check(assembly.GetName().Version == new Version(1, 0, 0, 0), "assembly version");
        var version = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(assembly, typeof(AssemblyInformationalVersionAttribute));
        Check(version.InformationalVersion == "1.0.0", "release version");
        using (var stream = assembly.GetManifestResourceStream("VoiceWinBridge.AppIcon"))
        using (var reader = new BinaryReader(stream))
        {
            Check(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1, "embedded ICO header");
            int count = reader.ReadUInt16();
            var sizes = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                int width = reader.ReadByte();
                reader.ReadBytes(15);
                sizes.Add(width == 0 ? 256 : width);
            }
            Check(sizes.Contains(16) && sizes.Contains(32) && sizes.Contains(48) && sizes.Contains(256), "multi-size embedded icon");
        }
        IntPtr module = LoadLibraryEx(executable, IntPtr.Zero, 0x22); // data/image resources only
        Check(module != IntPtr.Zero, "open PE resources");
        try
        {
            bool found = false;
            EnumResourceNames(module, new IntPtr(14), delegate { found = true; return false; }, IntPtr.Zero);
            Check(found, "PE RT_GROUP_ICON");
        }
        finally { FreeLibrary(module); }

        var identity = assembly.GetType("AppIdentity");
        var icon = (Icon)identity.GetField("Icon", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        Check(icon != null, "runtime app icon");
        var settingsType = assembly.GetType("BridgeSettings");
        object settings = Activator.CreateInstance(settingsType, true);
        var logging = settingsType.GetProperty("EnableFileLogging");
        Check((bool)logging.GetValue(settings, null), "existing logging default preserved");
        var serializer = new DataContractJsonSerializer(settingsType);
        using (var memory = new MemoryStream())
        {
            logging.SetValue(settings, false, null);
            serializer.WriteObject(memory, settings);
            memory.Position = 0;
            object restored = serializer.ReadObject(memory);
            Check(!(bool)logging.GetValue(restored, null), "file logging off persists");
            Check((bool)settingsType.GetProperty("EnableWin").GetValue(restored, null), "existing trigger setting preserved");
        }
        using (var memory = new MemoryStream(Encoding.UTF8.GetBytes("{\"EnableWin\":true,\"EnableRightAlt\":true,\"HoldMilliseconds\":520,\"InstallDirectory\":\"\"}")))
            Check((bool)logging.GetValue(serializer.ReadObject(memory), null), "legacy configuration keeps logging default");
        logging.SetValue(settings, true, null);
        var main = assembly.GetType("VoiceWinBridge");
        var enabled = main.GetField("fileLoggingEnabled", BindingFlags.Static | BindingFlags.NonPublic);
        var startLog = main.GetMethod("StartLogWriter", BindingFlags.Static | BindingFlags.NonPublic);
        var stopLog = main.GetMethod("StopLogWriter", BindingFlags.Static | BindingFlags.NonPublic);
        var log = main.GetMethod("Log", BindingFlags.Static | BindingFlags.NonPublic);
        string logPath = (string)main.GetField("LogPath", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        DateTime priorWrite = File.GetLastWriteTimeUtc(logPath);
        enabled.SetValue(null, false);
        startLog.Invoke(null, null);
        log.Invoke(null, new object[] { "disabled logging sample" });
        stopLog.Invoke(null, null);
        Check(File.GetLastWriteTimeUtc(logPath) == priorWrite, "file logging off performs no write");
        enabled.SetValue(null, true);
        startLog.Invoke(null, null);
        log.Invoke(null, new object[] { "release smoke logging sample" });
        stopLog.Invoke(null, null);
        Check(File.ReadAllText(logPath).Contains("release smoke logging sample"), "file logging on writes diagnostics");
        startLog.Invoke(null, null);
        File.WriteAllText(logPath, new string('x', 1024 * 1024));
        log.Invoke(null, new object[] { "rotation smoke sample" });
        stopLog.Invoke(null, null);
        Check(File.ReadAllText(logPath).Contains("rotation smoke sample"), "logging rotates at size limit");
        Check(new FileInfo(logPath + ".1").Length == 1024 * 1024, "one retained history file");
        Application.EnableVisualStyles();
        using (var form = (Form)Activator.CreateInstance(assembly.GetType("SettingsForm"), new object[] { settings }))
        {
            Check(form.Text.Contains("v1.0.0") && form.Text.Contains("C#"), "window identity");
            Check(form.Icon == icon, "window and tray share app icon");
            Check((bool)form.GetType().GetProperty("EnableFileLogging").GetValue(form, null), "logging checkbox binds saved value");
            form.Show();
            Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(args[1], "settings-preview.png"));
            }
            form.Close();
        }
        Console.WriteLine("PASS " + checks + " release resource/version/UI checks");
    }

    private delegate bool EnumResourceName(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumResourceName callback, IntPtr parameter);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
}
