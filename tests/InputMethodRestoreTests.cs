using System;
using System.Runtime.InteropServices;

internal static class InputMethodRestoreTests
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        checks++;
    }
    private static void Main()
    {
        var sogou = new InputMethodProfile { Type = 1, Language = 0x0804,
            Clsid = new Guid("e7ea138e-69f8-11d7-a6ea-00065b844310"),
            ProfileGuid = new Guid("e7ea138f-69f8-11d7-a6ea-00065b844311") };
        var doubao = new InputMethodProfile { Type = 1, Language = 0x0804,
            Clsid = new Guid("9d2b2e2b-3c93-4d2f-9d35-6eeb85f0d2b0"),
            ProfileGuid = new Guid("2b4d4b3a-4d4f-4c0a-8e66-7f771a2b9c10") };
        Check(!sogou.SameAs(doubao), "different Chinese TIPs with identical HKL");
        Check(sogou.SameAs(sogou), "same TIP");
        var otherProfile = sogou; otherProfile.ProfileGuid = Guid.NewGuid();
        Check(!sogou.SameAs(otherProfile), "profile GUID participates in identity");
        Check(InputMethodRestore.Decide(sogou, doubao, true, true) == "restore", "restore original");
        Check(InputMethodRestore.Decide(sogou, doubao, false, true) == "foreground-changed", "do not restore another window");
        Check(InputMethodRestore.Decide(sogou, sogou, true, false) == "already-original", "no redundant activation");
        Check(InputMethodRestore.Decide(sogou, otherProfile, true, false) == "another-profile-selected", "preserve manual selection");
        Check(InputMethodRestore.Decide(doubao, doubao, true, true) == "already-original", "original Doubao unchanged");
        var english = new InputMethodProfile { Type = 2, Language = 0x0409, Hkl = new IntPtr(0x04090409) };
        Check(english.SameAs(english), "same keyboard layout");
        var otherLayout = english; otherLayout.Hkl = new IntPtr(0x08040804);
        Check(!english.SameAs(otherLayout), "layout handle participates in identity");
        Check(InputMethodRestore.Decide(english, doubao, true, true) == "restore", "restore original non-Chinese layout");
        Check(Marshal.SizeOf(typeof(InputMethodProfile)) == (IntPtr.Size == 8 ? 88 : 72), "SDK native structure layout");
        Check(InputMethodProfiles.ForSession == 0x20000000, "SDK session flag, not enable/disable registry flags");
        Console.WriteLine("PASS " + checks + " input-method identity/restore-guard checks");
    }
}
