using System.Globalization;
using System.Text.Json;

namespace Axiom.Infrastructure;

public static class DiagnosticsPresentation
{
    public static string Summarize(string raw)
    {
        var lines = new List<string>();
        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var json = JsonDocument.Parse(line);
                var entry = json.RootElement;
                if (entry.ValueKind != JsonValueKind.Object) { lines.Add(line); continue; }
                var level = Read(entry, "level") ?? "Information";
                var eventName = Read(entry, "eventName") ?? "Unspecified event";
                var timestamp = Read(entry, "timestampUtc");
                var displayTime = DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                    ? time.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
                    : "Time unavailable";
                var action = eventName switch
                {
                    "application.started" => "Application opened",
                    "application.stopped" => "Application closed normally",
                    "preview.scroll.sync.failed" => "Could not synchronize editor and preview scrolling",
                    "preview.live.replace.failed" => "Could not refresh Preview in place; full refresh was attempted",
                    "application.activation.failed" => "Could not open a document requested by another launch",
                    _ => string.Join(' ', eventName.Split('.', StringSplitOptions.RemoveEmptyEntries))
                };
                lines.Add($"{displayTime} · {level} · {action}");
                if (Read(entry, "path") is { Length: > 0 } path) lines.Add("Document: " + path);
                if (Read(entry, "error") is { Length: > 0 } error)
                {
                    lines.Add("Reported problem: " + error);
                    lines.Add(error.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                        ? "Next step: close other Axiom windows using the same state, then retry. If another program holds the file, resolve that lock first. Unsaved work should be preserved before closing."
                        : "Next step: review the reported problem and retry after resolving it. Show technical details when reporting this issue.");
                }
                lines.Add("");
            }
            catch (JsonException) { lines.Add("Unstructured diagnostic entry: " + line); }
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string? Read(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
