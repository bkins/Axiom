namespace Axiom.App;

public partial class MainWindow
{
    private async Task RenderMermaidAsync()
    {
        if (_previewLinkToken is not { } token || !_previewReady) return;
        if (_previewLinkStructure?.Nodes.Any(node => node.Kind == Axiom.Core.DocumentStructureNodeKind.Code && string.Equals(node.Language, "mermaid", StringComparison.OrdinalIgnoreCase)) != true) return;
        try
        {
            await Preview.InvokeScript(MermaidPreviewRenderer.BuildScript(token, ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark));
        }
        catch (Exception exception)
        {
            _log.Error("preview.mermaid.failed", exception, _session.Path);
            // Encoded fenced source remains available when the native renderer fails.
        }
    }
}
