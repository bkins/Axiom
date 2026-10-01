using System.Text.RegularExpressions;

namespace Axiom.Core;

public sealed record DocumentFile(
    string Path,
    string Text,
    DateTimeOffset LastWriteUtc,
    long Length)
{
    public string ObservedEncoding { get; init; } = "Unknown encoding";
}

public sealed record RecoveryDraft(
    string DraftKey,
    string? FilePath,
    string Text,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? SavedFileWriteUtc,
    bool IsNewDocument);

public sealed record ExternalConflict(
    string LocalText,
    string ExternalText,
    DateTimeOffset ExternalWriteUtc);

public sealed record DocumentIdentityOccurrence(Guid DocumentId, string Path);

public sealed record MarkdownDiscoveryResult(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> SkippedDirectories);

public sealed class DocumentSession
{
    private DocumentSession(string draftKey, string? path, string text, DateTimeOffset? writeUtc)
    {
        DraftKey = draftKey;
        Path = path;
        Text = text;
        SavedText = text;
        LastKnownWriteUtc = writeUtc;
    }

    public string DraftKey { get; }
    public string? Path { get; private set; }
    public string Text { get; private set; }
    public string SavedText { get; private set; }
    public string ObservedEncoding { get; private set; } = "Unsaved — writes UTF-8";
    public DateTimeOffset? LastKnownWriteUtc { get; private set; }
    public ExternalConflict? Conflict { get; private set; }
    public bool IsDirty => !string.Equals(Text, SavedText, StringComparison.Ordinal);
    public bool IsNew => Path is null;
    public Guid? DocumentId => DocumentIdCodec.Find(Text);

    public static DocumentSession New() => new($"unsaved:{Guid.NewGuid():N}", null, string.Empty, null);

    public static DocumentSession Open(DocumentFile file) =>
        new($"file:{System.IO.Path.GetFullPath(file.Path).ToUpperInvariant()}", file.Path, file.Text, file.LastWriteUtc)
        {
            ObservedEncoding = file.ObservedEncoding
        };

    public static DocumentSession Recover(RecoveryDraft draft, string savedText = "") =>
        new(draft.DraftKey, draft.FilePath, draft.Text, draft.SavedFileWriteUtc)
        {
            SavedText = savedText
          , ObservedEncoding = draft.FilePath is null ? "Unsaved — writes UTF-8" : "Recovered — encoding unobserved"
        };

    public void UpdateText(string text)
    {
        Text = text ?? string.Empty;
    }

    public void MarkSaved(string path, DateTimeOffset writeUtc)
    {
        Path = path;
        SavedText = Text;
        LastKnownWriteUtc = writeUtc;
        ObservedEncoding = "UTF-8 (no BOM)";
        Conflict = null;
    }

    public void Reload(DocumentFile file)
    {
        Path = file.Path;
        Text = file.Text;
        SavedText = file.Text;
        ObservedEncoding = file.ObservedEncoding;
        LastKnownWriteUtc = file.LastWriteUtc;
        Conflict = null;
    }

    public void RecordConflict(DocumentFile external) =>
        Conflict = new ExternalConflict(Text, external.Text, external.LastWriteUtc);

    public void KeepLocalAfterConflict()
    {
        if (Conflict is null) return;
        LastKnownWriteUtc = Conflict.ExternalWriteUtc;
        SavedText = Conflict.ExternalText;
        Conflict = null;
    }

    public void DiscardChanges()
    {
        Text = SavedText;
        Conflict = null;
    }

    public void UpdatePath(string path) => Path = path;
}

public static partial class DocumentIdCodec
{
    private const string Prefix = "<!-- axiom:documentId=";

    // Axiom writes its marker as the first line. Anchoring it prevents examples in
    // fenced code blocks (or ordinary prose) from being mistaken for document data.
    [GeneratedRegex(@"\A\uFEFF?<!--\s*axiom:documentId=(?<id>[0-9a-fA-F-]{36})\s*-->\r?\n?", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityRegex();

    public static Guid? Find(string markdown)
    {
        var match = IdentityRegex().Match(markdown ?? string.Empty);
        return match.Success && Guid.TryParse(match.Groups["id"].Value, out var id) && id != Guid.Empty ? id : null;
    }

    public static string Enroll(string markdown, Guid id)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        if (Find(markdown) is not null) return markdown;
        var separator = string.IsNullOrEmpty(markdown) ? string.Empty : Environment.NewLine + Environment.NewLine;
        return $"{Prefix}{id:D} -->{separator}{markdown}";
    }

    public static string Reassign(string markdown, Guid id)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        var marker = $"{Prefix}{id:D} -->";
        return IdentityRegex().IsMatch(markdown) ? IdentityRegex().Replace(markdown, marker, 1) : Enroll(markdown, id);
    }

    public static string RemoveCanonicalMarkerForPreview(string markdown) =>
        IdentityRegex().Replace(markdown ?? string.Empty, string.Empty, 1);
}
