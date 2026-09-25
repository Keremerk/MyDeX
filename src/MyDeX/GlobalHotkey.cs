using System.Runtime.InteropServices;

namespace MyDeX;

/// <summary>A system-wide keyboard shortcut (works while any other app has focus).</summary>
static partial class GlobalHotkey
{
    public const int WM_HOTKEY = 0x0312;
    public const int Id = 0x4D59;   // "MY"

    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    const uint VK_D = 0x44;

    public const string Description = "Ctrl+Alt+D";

    /// <summary>Returns false if another program already uses the shortcut.</summary>
    public static bool Register(IntPtr window) => RegisterHotKey(window, Id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_D);

    public static void Unregister(IntPtr window) => UnregisterHotKey(window, Id);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);
}
