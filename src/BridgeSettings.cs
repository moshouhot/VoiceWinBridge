using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;

[DataContract]
internal sealed class BridgeSettings
{
    private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "DoubaoVoiceWinBridge";
    private static readonly string SettingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    [DataMember] public bool EnableWin { get; set; }
    [DataMember] public bool EnableRightAlt { get; set; }
    [DataMember] public int HoldMilliseconds { get; set; }
    [DataMember] public string InstallDirectory { get; set; }
    [IgnoreDataMember] public bool StartWithWindows { get; set; }

    public BridgeSettings()
    {
        EnableWin = true;
        EnableRightAlt = true;
        HoldMilliseconds = 520;
        InstallDirectory = DetectInstallDirectory();
        StartWithWindows = IsStartWithWindowsEnabled();
    }

    public static BridgeSettings Load(Action<string> log)
    {
        if (!File.Exists(SettingsPath))
            return new BridgeSettings();
        try
        {
            using (var stream = File.OpenRead(SettingsPath))
            {
                var serializer = new DataContractJsonSerializer(typeof(BridgeSettings));
                var value = (BridgeSettings)serializer.ReadObject(stream);
                value.HoldMilliseconds = Math.Max(300, Math.Min(1500, value.HoldMilliseconds));
                if (!IsInstallDirectory(value.InstallDirectory))
                    value.InstallDirectory = DetectInstallDirectory();
                value.StartWithWindows = IsStartWithWindowsEnabled();
                return value;
            }
        }
        catch (Exception ex)
        {
            log("settings load failed: " + ex.Message);
            return new BridgeSettings();
        }
    }

    public void Save()
    {
        using (var stream = File.Create(SettingsPath))
        {
            var serializer = new DataContractJsonSerializer(typeof(BridgeSettings));
            serializer.WriteObject(stream, this);
        }
    }

    internal static bool IsStartWithWindowsEnabled()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath))
            {
                string command = key == null ? null : key.GetValue(StartupValueName) as string;
                string registeredPath = ExtractExecutablePath(command);
                string currentPath = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
                return !String.IsNullOrEmpty(registeredPath)
                    && String.Equals(Path.GetFullPath(registeredPath), currentPath,
                        StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            return false;
        }
    }

    internal static void SetStartWithWindows(bool enabled)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath))
        {
            if (enabled)
            {
                string executable = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
                key.SetValue(StartupValueName, "\"" + executable + "\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(StartupValueName, false);
            }
        }
    }

    private static string ExtractExecutablePath(string command)
    {
        if (String.IsNullOrWhiteSpace(command))
            return null;
        command = command.Trim();
        if (command[0] == '"')
        {
            int closingQuote = command.IndexOf('"', 1);
            return closingQuote > 1 ? command.Substring(1, closingQuote - 1) : null;
        }
        int separator = command.IndexOf(' ');
        return separator > 0 ? command.Substring(0, separator) : command;
    }

    internal static string DetectInstallDirectory()
    {
        try
        {
            foreach (Process process in Process.GetProcessesByName("ImeService"))
            {
                try
                {
                    string directory = Path.GetDirectoryName(process.MainModule.FileName);
                    if (IsInstallDirectory(directory))
                        return directory;
                }
                catch { }
                finally { process.Dispose(); }
            }
        }
        catch { }

        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey uninstall = root.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (uninstall == null) continue;
                        foreach (string name in uninstall.GetSubKeyNames())
                        {
                            using (RegistryKey item = uninstall.OpenSubKey(name))
                            {
                                string displayName = item == null ? null : item.GetValue("DisplayName") as string;
                                if (String.IsNullOrEmpty(displayName)
                                    || (displayName.IndexOf("豆包输入法", StringComparison.OrdinalIgnoreCase) < 0
                                    && displayName.IndexOf("DoubaoIME", StringComparison.OrdinalIgnoreCase) < 0))
                                    continue;
                                string directory = item.GetValue("InstallLocation") as string;
                                if (IsInstallDirectory(directory))
                                    return directory.TrimEnd('\\');
                            }
                        }
                    }
                }
                catch { }
            }
        }

        string fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DoubaoIME");
        return IsInstallDirectory(fallback) ? fallback : String.Empty;
    }

    internal static bool IsInstallDirectory(string directory)
    {
        return !String.IsNullOrWhiteSpace(directory)
            && File.Exists(Path.Combine(directory, "rpc.dll"))
            && File.Exists(Path.Combine(directory, "ImeService.exe"));
    }
}
