using Microsoft.Win32;
namespace Classroom;
static class StartupRegistration
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static string Command(string executable) => "\"" + executable + "\"";
    public static bool Enabled() { using var key = Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue("ClassroomStudent") is string text && !string.IsNullOrWhiteSpace(text); }
    public static void Enable()
    {
        var executable = Environment.ProcessPath ?? throw new IOException("Путь к Student.exe неизвестен.");
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) throw new IOException("Для автозапуска запустите Student.exe, а не DLL через dotnet.");
        using var key = Registry.CurrentUser.CreateSubKey(Key); key.SetValue("ClassroomStudent", Command(executable), RegistryValueKind.String);
    }
    public static void Disable() { using var key = Registry.CurrentUser.OpenSubKey(Key, true); key?.DeleteValue("ClassroomStudent", false); }
}
