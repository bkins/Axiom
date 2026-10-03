namespace Axiom.App;

public partial class MainWindow
{
    internal async Task OpenForwardedDocumentAsync(string path)
    {
        try { await OpenDocumentAsync(path); }
        catch (Exception exception) { ReportError("application.activation.failed", exception, path); }
    }
}
