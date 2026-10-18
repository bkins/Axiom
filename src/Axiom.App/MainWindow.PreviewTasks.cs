using System.Globalization;
using Axiom.Core;

namespace Axiom.App;

public partial class MainWindow
{
    private bool HandlePreviewTask(Uri request)
    {
        var parts = request.AbsolutePath.Trim('/').Split('/');
        if (request.Scheme != "axiom-task" || request.Host != "toggle" || parts.Length != 3 ||
            !string.IsNullOrEmpty(request.Query) || !string.IsNullOrEmpty(request.Fragment) ||
            parts[0] != _previewLinkToken || _previewLinkStructure is not { } rendered ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var offset) ||
            parts[2] is not ("0" or "1") || _sessionTransitionRunning || _navigationRunning || _metadataApplying || _workspaceBatchRunning ||
            _closingApproved || Editor.IsReadOnly ||
            !PreviewTaskToggle.TryToggle(rendered, Editor.Text, offset, parts[2] == "1", out var replacement)) return false;
        // Invalidate the old view immediately: repeated/stale clicks cannot target newer text.
        _previewLinkToken = null;
        Editor.Document.UndoStack.StartUndoGroup();
        try { Editor.Document.Replace(offset, 1, replacement.ToString()); }
        finally { Editor.Document.UndoStack.EndUndoGroup(); }
        UpdateChrome(replacement == 'x' ? "Task checked. Changes are unsaved." : "Task unchecked. Changes are unsaved.");
        return true;
    }

    internal string PrepareTaskPreviewForTesting()
    {
        _previewLinkStructure = GetCurrentStructure();
        return _previewLinkToken = Guid.NewGuid().ToString("N");
    }
    internal bool TogglePreviewTaskForTesting(Uri request) => HandlePreviewTask(request);
}
