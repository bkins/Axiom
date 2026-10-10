using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private readonly SemaphoreSlim _externalOpenGate = new(1, 1);

    internal async Task OpenForwardedDocumentAsync(string path)
    {
        await StartupCompleted;
        try { await OpenExternalDocumentAsync(path); }
        catch (Exception exception) { ReportError("application.activation.failed", exception, path); }
    }

    internal async Task<bool> OpenExternalDocumentAsync(string path)
    {
        await _externalOpenGate.WaitAsync();
        try
        {
            path = Path.GetFullPath(path);
            if (Path.GetExtension(path).ToLowerInvariant() is not (".md" or ".markdown") || !File.Exists(path))
            {
                UpdateChrome("The requested Markdown file is missing or unsupported; current documents retained.");
                return false;
            }
            if (_sessionTransitionRunning || _navigationRunning || _metadataApplying) return false;
            var catalog = ManagementState is null ? [] : await ManagementState.ListWorkspacesAsync();
            var workspace = WorkspaceDocumentResolver.Resolve(path, catalog.Where(item => Directory.Exists(item.RootPath)), _folder);
            if (workspace is not null)
            {
                var inspected = await new MarkdownLinkTargetReader(_files).ReadAsync(workspace.RootPath, path);
                if (inspected.Status != MarkdownLinkResolutionStatus.Resolved)
                {
                    UpdateChrome(inspected.Message ?? "The requested workspace document is unavailable; current documents retained.");
                    return false;
                }
            }
            if (workspace is not null && !await SwitchWorkspaceAsync(workspace.RootPath)) return false;
            await OpenDocumentAsync(path);
            if (_session.Path is null || !Path.GetFullPath(_session.Path).Equals(path, WorkspacePathBoundary.Comparison)) return false;
            if (_folder is not null && WorkspacePathBoundary.Contains(_folder, path))
            {
                // An explicit OS request must reveal its file, even when a remembered filter hides it.
                if (!_explorerDocuments.ContainsKey(Path.GetRelativePath(_folder, path)))
                {
                    ExplorerFilter.Text = "";
                    RefreshExplorer();
                }
                RevealActiveExplorerDocument();
            }
            return true;
        }
        catch (Exception exception)
        {
            ReportError("application.document.open.failed", exception, path);
            return false;
        }
        finally { _externalOpenGate.Release(); }
    }
}
