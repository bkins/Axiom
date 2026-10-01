using System.Text.RegularExpressions;

namespace Axiom.Core;

public static partial class DocumentStatistics
{
    public static int CountSourceWords(string text) => WordPattern().Count(DocumentIdCodec.RemoveCanonicalMarkerForPreview(text));

    public static string DescribeNewlines(string text)
    {
        var crlf = false;
        var lf = false;
        var cr = false;
        for (var position = 0; position < text.Length; position++)
        {
            if (text[position] == '\r')
            {
                if (position + 1 < text.Length && text[position + 1] == '\n') { crlf = true; position++; }
                else cr = true;
            }
            else if (text[position] == '\n') lf = true;
        }
        var kinds = (crlf ? 1 : 0) + (lf ? 1 : 0) + (cr ? 1 : 0);
        return kinds > 1 ? "Mixed newlines" : crlf ? "CRLF" : lf ? "LF" : cr ? "CR" : "No newline";
    }

    // Source count includes code, URLs and image/link text. A contiguous run of
    // Unicode letters/digits is one word; internal apostrophes stay in the run.
    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();
}
