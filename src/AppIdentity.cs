using System;
using System.Drawing;
using System.Reflection;

internal static class AppIdentity
{
    internal static readonly string Version = ((AssemblyInformationalVersionAttribute)
        Attribute.GetCustomAttribute(typeof(AppIdentity).Assembly, typeof(AssemblyInformationalVersionAttribute))).InformationalVersion;
    internal static readonly Icon Icon = LoadIcon();

    private static Icon LoadIcon()
    {
        using (var stream = typeof(AppIdentity).Assembly.GetManifestResourceStream("VoiceWinBridge.AppIcon"))
        using (var icon = new Icon(stream))
            return (Icon)icon.Clone();
    }
}
