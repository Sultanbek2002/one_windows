using System.Runtime.InteropServices;

namespace Classroom;
sealed class ScreenBlocker : IDisposable
{
    readonly List<Form> covers = [];
    readonly HashSet<int> keys = [];
    readonly Hook keyboard, mouse;
    IntPtr keyboardHook, mouseHook;
    public bool Active => covers.Count > 0;
    public event Action? EmergencyExit;
    public ScreenBlocker() { keyboard = Keyboard; mouse = Mouse; }
    IntPtr Mouse(int code, IntPtr message, IntPtr data) => code < 0 ? CallNextHookEx(mouseHook, code, message, data) : (IntPtr)1;
    public void Activate()
    {
        if (Active) return;
        try {
            foreach (var monitor in Screen.AllScreens) {
                var cover = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = monitor.Bounds, TopMost = true, ShowInTaskbar = false, BackColor = Color.FromArgb(20, 28, 42) };
                cover.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Segoe UI", 24), Text = "Экран заблокирован учителем\n\nПожалуйста, обратите внимание на учителя.\n\nАварийный выход: Ctrl + Alt + Shift + F12" });
                cover.FormClosing += (_, e) => { if (Active && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };
                covers.Add(cover); cover.Show();
            }
            keyboardHook = SetWindowsHookEx(13, keyboard, GetModuleHandle(null), 0);
            if (keyboardHook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            mouseHook = SetWindowsHookEx(14, mouse, GetModuleHandle(null), 0);
            if (mouseHook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        } catch { Dispose(); throw; }
    }
    IntPtr Keyboard(int code, IntPtr message, IntPtr data)
    {
        if (code < 0) return CallNextHookEx(keyboardHook, code, message, data);
        var key = Marshal.ReadInt32(data);
        if ((int)message is 0x100 or 0x104) keys.Add(key); else keys.Remove(key);
        if (key == 123 && keys.Overlaps([162, 163]) && keys.Overlaps([164, 165]) && keys.Overlaps([160, 161])) {
            var owner = covers.FirstOrDefault();
            owner?.BeginInvoke((Action)(() => EmergencyExit?.Invoke()));
        }
        return (IntPtr)1;
    }
    public void Dispose()
    {
        if (keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
        if (mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
        var old = covers.ToArray(); covers.Clear(); keys.Clear();
        foreach (var cover in old) cover.Dispose();
    }
    delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int type, Hook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
}
