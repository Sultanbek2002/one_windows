using System.Text.Json;
namespace Classroom;
sealed class Settings
{
    public Dictionary<string, string> Trusted { get; set; } = [];
    public bool AutoControl { get; set; } = true;
    public int VideoMode { get; set; } = 1;
    public int PermissionDefaultsVersion { get; set; }
    public int StartupConfiguredVersion { get; set; }
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Classroom");
    public static string PathFor(string role) => Path.Combine(DirectoryPath, role + ".json");
    public static Settings Load(string role) => File.Exists(PathFor(role)) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathFor(role))) ?? new() : new();
    public void Save(string role) { Directory.CreateDirectory(DirectoryPath); var path = PathFor(role); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this)); File.Move(path + ".tmp", path, true); }
}
