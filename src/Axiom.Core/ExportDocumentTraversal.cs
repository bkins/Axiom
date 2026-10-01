namespace Axiom.Core;

public static class ExportDocumentTraversal
{
    public static IEnumerable<ExportBlock> Flatten(IEnumerable<ExportBlock> blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;
            foreach (var nested in Flatten(block.Children)) yield return nested;
        }
    }
}
