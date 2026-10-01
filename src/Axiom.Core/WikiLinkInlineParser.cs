using System.Text;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Axiom.Core;

internal sealed class WikiLinkInlineParser : InlineParser
{
    internal WikiLinkInlineParser() => OpeningCharacters = ['['];

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        if (slice.CurrentChar != '[' || slice.PeekChar() != '[') return false;
        var target = new StringBuilder();
        var label = new StringBuilder();
        var hasLabel = false;
        for (var index = slice.Start + 2; index <= slice.End && index - slice.Start <= 2048; index++)
        {
            var character = slice.Text[index];
            if (character is '\r' or '\n' or '[') return false;
            if (character == '\\' && index < slice.End && "|]\\".Contains(slice.Text[index + 1]))
            {
                (hasLabel ? label : target).Append(slice.Text[++index]);
                continue;
            }
            if (character == ']' && index < slice.End && slice.Text[index + 1] == ']')
            {
                var destination = target.ToString().Trim();
                var caption = hasLabel ? label.ToString().Trim() : destination;
                if (destination.Length == 0 || caption.Length == 0) return false;
                var start = processor.GetSourcePosition(slice.Start, out var line, out var column);
                var link = new LinkInline
                {
                    Url = WikiLinkResolver.Prefix + Uri.EscapeDataString(destination)
                  , Span = new SourceSpan(start, processor.GetSourcePosition(index + 1))
                  , Line = line
                  , Column = column
                  , IsClosed = true
                };
                link.AppendChild(new LiteralInline(caption));
                processor.Inline = link;
                slice.Start = index + 2;
                return true;
            }
            if (character == '|')
            {
                if (hasLabel) return false;
                hasLabel = true;
            }
            else (hasLabel ? label : target).Append(character);
        }
        return false;
    }
}
