using System.Net;
using System.Text;

namespace Axiom.Core;

/// <summary>Bounded offline lexical colouring. This does not compile or execute source.</summary>
public static class CSharpSyntax
{
    private static readonly HashSet<string> Keywords = new(("abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while add alias and ascending async await by descending dynamic equals file from get global group init into join let managed nameof not notnull on or orderby partial record remove required scoped select set unmanaged value var when where with yield").Split(' '), StringComparer.Ordinal);

    public static bool IsLanguage(string? language) => language?.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() is "c#" or "csharp" or "cs";

    public static IReadOnlyList<CSharpToken> Tokenize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > 1_048_576) return [];
        var tokens = new List<CSharpToken>();
        var index = 0;
        while (index < source.Length && tokens.Count < 50_000)
        {
            var start = index;
            string? kind = null;
            var character = source[index];
            if (character == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                index += 2; while (index < source.Length && source[index] is not '\r' and not '\n') index++;
                kind = "comment";
            }
            else if (character == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                var close = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = close < 0 ? source.Length : close + 2; kind = "comment";
            }
            else if (TryString(source, index, out var end)) { index = end; kind = "string"; }
            else if (char.IsDigit(character))
            {
                index++;
                if (character == '0' && index < source.Length && source[index] is 'x' or 'X' or 'b' or 'B')
                {
                    index++;
                    while (index < source.Length && (char.IsAsciiHexDigit(source[index]) || source[index] == '_')) index++;
                }
                else
                {
                    while (index < source.Length && (char.IsDigit(source[index]) || source[index] == '_')) index++;
                    if (index + 1 < source.Length && source[index] == '.' && char.IsDigit(source[index + 1]))
                    {
                        index++; while (index < source.Length && (char.IsDigit(source[index]) || source[index] == '_')) index++;
                    }
                    if (index < source.Length && source[index] is 'e' or 'E')
                    {
                        index++; if (index < source.Length && source[index] is '+' or '-') index++;
                        while (index < source.Length && (char.IsDigit(source[index]) || source[index] == '_')) index++;
                    }
                }
                while (index < source.Length && "mMdDfFuUlL".Contains(source[index])) index++;
                kind = "number";
            }
            else if (char.IsLetter(character) || character is '_' or '@')
            {
                index++; while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] == '_')) index++;
                if (Keywords.Contains(source[start..index])) kind = "keyword";
            }
            else index++;
            if (kind is not null) tokens.Add(new(start, index - start, kind));
        }
        return tokens;
    }

    private static bool TryString(string source, int start, out int end)
    {
        end = start;
        var cursor = start;
        var verbatim = false;
        while (cursor < source.Length && source[cursor] is '$' or '@') { verbatim |= source[cursor] == '@'; cursor++; }
        if (cursor >= source.Length || source[cursor] is not '"' and not '\'' || cursor > start && source[cursor] == '\'') return false;
        var quote = source[cursor];
        var count = 1;
        while (quote == '"' && cursor + count < source.Length && source[cursor + count] == '"') count++;
        if (count >= 3 && !verbatim)
        {
            var delimiter = new string('"', count);
            var close = source.IndexOf(delimiter, cursor + count, StringComparison.Ordinal);
            end = close < 0 ? source.Length : close + count;
            return true;
        }
        cursor++;
        while (cursor < source.Length)
        {
            if (!verbatim && source[cursor] == '\\') { cursor = Math.Min(source.Length, cursor + 2); continue; }
            if (source[cursor] == quote)
            {
                cursor++;
                if (verbatim && cursor < source.Length && source[cursor] == quote) { cursor++; continue; }
                break;
            }
            if (!verbatim && source[cursor] is '\r' or '\n') break;
            cursor++;
        }
        end = cursor;
        return true;
    }

    public static string ToHtml(string source)
    {
        var result = new StringBuilder();
        var cursor = 0;
        foreach (var token in Tokenize(source))
        {
            result.Append(WebUtility.HtmlEncode(source[cursor..token.Offset]));
            result.Append("<span class=\"csharp-").Append(token.Kind).Append("\">");
            result.Append(WebUtility.HtmlEncode(source.Substring(token.Offset, token.Length))).Append("</span>");
            cursor = token.Offset + token.Length;
        }
        result.Append(WebUtility.HtmlEncode(source[cursor..]));
        return result.ToString();
    }
}
