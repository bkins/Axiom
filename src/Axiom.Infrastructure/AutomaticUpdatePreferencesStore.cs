using System.Text.Json;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed class AutomaticUpdatePreferencesStore(AppDataPaths paths, string defaultChannel = "stable")
{
    private string FilePath => Path.Combine(paths.Root, "automatic-updates.json");

    public AutomaticUpdatePreferences Load()
    {
        if (!File.Exists(FilePath)) return new(Channel: defaultChannel);
        var value = JsonSerializer.Deserialize<AutomaticUpdatePreferences>(File.ReadAllText(FilePath))
            ?? throw new IOException("Update settings are empty.");
        _ = value.FeedChannel;
        return value;
    }

    public void Save(AutomaticUpdatePreferences value)
    {
        _ = value.FeedChannel;
        Directory.CreateDirectory(paths.Root);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value));
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
