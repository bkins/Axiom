using System.Text;
using System.Text.RegularExpressions;
using Axiom.Core;

namespace Axiom.Infrastructure;

public static partial class MarkdownIdentityBytes
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Decode(byte[] bytes)
    {
        var offset = HasBom(bytes) ? 3 : 0;
        var text = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
        if (text.Contains('\0')) throw new IOException("Only UTF-8 Markdown can be enrolled or duplicated with a new identity.");
        return text;
    }

    public static byte[] Assign(byte[] original, Guid id, bool replace)
    {
        var text = Decode(original);
        var existing = DocumentIdCodec.Find(text);
        if (existing is not null && !replace) return original;
        var marker = $"<!-- axiom:documentId={id:D} -->";
        if (existing is not null)
        {
            var match = Marker().Match(text);
            text = marker + text[match.Length..];
        }
        else
        {
            if (text.StartsWith("<!--", StringComparison.Ordinal) && text.Split('\n')[0].Contains("axiom:documentId", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Resolve the malformed first-line identity before enrollment.");
            var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            text = marker + newline + text;
        }
        var content = StrictUtf8.GetBytes(text);
        return HasBom(original) ? new byte[] { 0xef, 0xbb, 0xbf }.Concat(content).ToArray() : content;
    }

    private static bool HasBom(byte[] bytes) => bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf });

    [GeneratedRegex(@"\A<!--\s*axiom:documentId=[0-9a-fA-F-]{36}\s*-->", RegexOptions.CultureInvariant)]
    private static partial Regex Marker();
}
