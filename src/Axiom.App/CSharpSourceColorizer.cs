using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Axiom.Core;

namespace Axiom.App;

internal sealed class CSharpSourceColorizer : DocumentColorizingTransformer
{
    internal IReadOnlyList<CSharpToken> Tokens { get; private set; } = [];
    internal bool Dark { get; set; }

    internal void Update(DocumentStructure structure)
    {
        var source = structure.SourceText;
        var tokens = new List<CSharpToken>();
        foreach (var code in structure.Nodes.Where(node => node.Kind == DocumentStructureNodeKind.Code && CSharpSyntax.IsLanguage(node.Language)))
        {
            var firstBreak = source.IndexOf('\n', code.Source.Offset);
            if (firstBreak < 0 || firstBreak >= code.Source.Offset + code.Source.Length) continue;
            var start = firstBreak + 1;
            var end = Math.Min(source.Length, code.Source.Offset + code.Source.Length);
            var lastBreak = source.LastIndexOf('\n', Math.Max(start, end - 1));
            if (lastBreak >= start && source[(lastBreak + 1)..end].TrimStart().StartsWith(source[code.Source.Offset..firstBreak].TrimStart()[..1], StringComparison.Ordinal)) end = lastBreak;
            if (end < start) continue;
            tokens.AddRange(CSharpSyntax.Tokenize(source[start..end]).Select(token => token with { Offset = token.Offset + start }));
        }
        Tokens = tokens.OrderBy(token => token.Offset).ToArray();
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var lower = 0;
        var upper = Tokens.Count;
        while (lower < upper)
        {
            var middle = lower + (upper - lower) / 2;
            if (Tokens[middle].Offset + Tokens[middle].Length <= line.Offset) lower = middle + 1;
            else upper = middle;
        }
        for (var index = lower; index < Tokens.Count; index++)
        {
            var token = Tokens[index];
            if (token.Offset >= line.EndOffset) break;
            if (token.Offset + token.Length <= line.Offset) continue;
            var colour = token.Kind switch
            {
                "keyword" => Dark ? "#C4B5FD" : "#6D28D9",
                "string" => Dark ? "#FCA5A5" : "#A32020",
                "comment" => Dark ? "#86EFAC" : "#166534",
                _ => Dark ? "#93C5FD" : "#1D4ED8"
            };
            ChangeLinePart(Math.Max(line.Offset, token.Offset), Math.Min(line.EndOffset, token.Offset + token.Length), element => element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(Color.Parse(colour))));
        }
    }
}
