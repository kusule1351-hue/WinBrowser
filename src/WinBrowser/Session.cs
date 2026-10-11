using System.Text.Json;

namespace WinBrowser;

/// <summary>Open tabs and sidebar state, restored on the next launch.</summary>
public sealed class Session
{
    public sealed class Tab
    {
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    public List<Tab> Tabs { get; set; } = [];
    public int ActiveIndex { get; set; }
    public bool SidebarVisible { get; set; } = true;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinBrowser", "session.json");

    public static Session? Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<Session>(File.ReadAllText(FilePath)) : null;
        }
        catch (Exception)
        {
            return null; // A corrupt session just means starting fresh.
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
