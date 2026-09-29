using System.Text.Json;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

public partial class MainWindow
{
    private readonly List<DocumentNavigationLocation> _backLocations = [];
    private readonly List<DocumentNavigationLocation> _forwardLocations = [];
    private readonly SemaphoreSlim _linkNavigationGate = new(1, 1);
    private string? _previewLinkToken;
    private DocumentStructure? _previewLinkStructure;
    private string? _pendingHeadingFragment;
    private bool _navigationRunning;

    private async void FollowLink_OnClick(object? sender, RoutedEventArgs eventArgs) => await FollowEditorLinkAsync();
    private async void Back_OnClick(object? sender, RoutedEventArgs eventArgs) => await NavigateHistoryAsync(false);
    private async void Forward_OnClick(object? sender, RoutedEventArgs eventArgs) => await NavigateHistoryAsync(true);

    private Task FollowEditorLinkAsync()
    {
        var link = GetCurrentStructure().FindLinkAt(Editor.CaretOffset);
        if (link is not null) return FollowDocumentLinkAsync(link.Destination);
        UpdateChrome("Place the editor caret inside a Markdown link to follow it.");
        return Task.CompletedTask;
    }

    private async Task HandlePreviewLinkAsync(Uri request)
    {
        var parts = request.AbsolutePath.Trim('/').Split('/');
        if (_sessionTransitionRunning || request.Host != "follow" || parts.Length != 2
            || parts[0] != _previewLinkToken || !int.TryParse(parts[1], out var index)
            || _previewLinkStructure is not { } rendered || index < 0 || index >= rendered.Links.Count) return;
        var oldLink = rendered.Links[index];
        var current = GetCurrentStructure().Links.FirstOrDefault(link => link.NodeId == oldLink.NodeId);
        if (current is null || current.Destination != oldLink.Destination)
        {
            UpdateChrome("The preview link changed. Wait for the refreshed preview and try again.");
            return;
        }
        await FollowDocumentLinkAsync(current.Destination);
    }

    private async Task FollowDocumentLinkAsync(string destination)
    {
        if (_sessionTransitionRunning || !await _linkNavigationGate.WaitAsync(0)) return;
        _navigationRunning = true;
        SetSessionCommandsEnabled(false);
        var source = _activeTab;
        var root = _folder;
        try
        {
            var resolver = new MarkdownLinkResolver(new MarkdownLinkTargetReader(_files));
            var result = await resolver.ResolveAsync(root, source.Session.Path, destination, path =>
            {
                if (FindTab(path) is not { } open) return null;
                return open == _activeTab ? GetCurrentStructure() : _structureParser.Parse(open.Session.Text, open.Structure);
            });
            if (_sessionTransitionRunning || _activeTab != source || _folder != root) return;
            if (result.Status == MarkdownLinkResolutionStatus.MissingHeading)
            {
                var choice = MissingHeadingChoiceForTesting is { } testChoice ? await testChoice(result)
                    : await ChoiceDialog.ShowAsync(this, "Heading not found", result.Message, "Open document", "Cancel");
                if (choice != "Open document") { UpdateChrome(result.Message); return; }
            }
            else if (result.Status != MarkdownLinkResolutionStatus.Resolved)
            {
                UpdateChrome(result.Message);
                return;
            }
            var previous = CurrentNavigationLocation();
            await OpenDocumentAsync(result.TargetPath!);
            if (!string.Equals(_session.Path, result.TargetPath, StringComparison.OrdinalIgnoreCase)) return;
            var offset = result.Source?.Offset ?? 0;
            if (result.Fragment is { Length: > 0 } fragment && result.Status == MarkdownLinkResolutionStatus.Resolved)
                offset = GetCurrentStructure().FindHeading(fragment)?.Source.Offset ?? 0;
            SetNavigationPosition(offset);
            if (previous is not null) _backLocations.Add(previous);
            _forwardLocations.Clear();
            _pendingHeadingFragment = result.Status == MarkdownLinkResolutionStatus.Resolved ? result.Fragment : null;
            if (_previewReady) await RestoreHeadingFragmentAsync();
            UpdateChrome(result.Status == MarkdownLinkResolutionStatus.MissingHeading ? "Opened the document; the requested heading is missing." : "Followed Markdown link; working buffers retained.");
            UpdateNavigationCommands();
        }
        catch (Exception exception) { ReportError("navigation.link.failed", exception, source.Session.Path); }
        finally
        {
            _navigationRunning = false;
            SetSessionCommandsEnabled(true);
            UpdateNavigationCommands();
            _linkNavigationGate.Release();
        }
    }

    private DocumentNavigationLocation? CurrentNavigationLocation() => _session.Path is { } path ? new(path, Editor.CaretOffset) : null;

    private void SetNavigationPosition(int offset)
    {
        offset = Math.Clamp(offset, 0, Editor.Document.TextLength);
        Editor.Select(offset, 0);
        Editor.CaretOffset = offset;
        Editor.ScrollToLine(Editor.Document.GetLineByOffset(offset).LineNumber);
        CaptureEditorState(_activeTab);
    }

    private async Task NavigateHistoryAsync(bool forward)
    {
        if (_sessionTransitionRunning || !await _linkNavigationGate.WaitAsync(0)) return;
        _navigationRunning = true;
        SetSessionCommandsEnabled(false);
        try
        {
            var from = forward ? _forwardLocations : _backLocations;
            var to = forward ? _backLocations : _forwardLocations;
            var current = CurrentNavigationLocation();
            var root = _folder;
            var reader = new MarkdownLinkTargetReader(_files);
            var skipped = 0;
            while (from.Count > 0)
            {
                var location = from[^1];
                from.RemoveAt(from.Count - 1);
                if (root is null || (await reader.ReadAsync(root, location.Path)).Status != MarkdownLinkResolutionStatus.Resolved)
                {
                    ++skipped;
                    UpdateChrome("Skipped an unavailable navigation history location.");
                    continue;
                }
                if (_sessionTransitionRunning || root != _folder) return;
                await OpenDocumentAsync(location.Path);
                if (!string.Equals(_session.Path, location.Path, StringComparison.OrdinalIgnoreCase)) continue;
                SetNavigationPosition(location.CaretOffset);
                if (current is not null) to.Add(current);
                UpdateChrome(skipped == 0 ? "Returned to a document location; working buffers retained."
                    : $"Returned to a document location; skipped {skipped} unavailable history locations.");
                break;
            }
            UpdateNavigationCommands();
        }
        catch (Exception exception) { ReportError("navigation.history.failed", exception, _folder); }
        finally
        {
            _navigationRunning = false;
            SetSessionCommandsEnabled(true);
            UpdateNavigationCommands();
            _linkNavigationGate.Release();
        }
    }

    private void UpdateNavigationCommands()
    {
        BackButton.IsEnabled = _backLocations.Count > 0;
        ForwardButton.IsEnabled = _forwardLocations.Count > 0;
    }

    private async Task RestoreHeadingFragmentAsync()
    {
        if (_pendingHeadingFragment is not { Length: > 0 } fragment || !_previewReady) return;
        var argument = JsonSerializer.Serialize(fragment);
        var token = JsonSerializer.Serialize(_previewLinkToken);
        var found = await Preview.InvokeScript($"(() => {{ if (document.documentElement.dataset.linkToken !== {token}) return false; const heading = document.getElementById({argument}); if (!heading) return false; heading.scrollIntoView(); return true; }})()");
        if (found?.Trim().Trim('"') == "true") _pendingHeadingFragment = null;
    }

    internal Task FollowLinkForTestingAsync(string destination) => FollowDocumentLinkAsync(destination);
    internal DocumentStructure StructureForTesting => GetCurrentStructure();
    internal Task FollowEditorLinkForTestingAsync() => FollowEditorLinkAsync();
    internal Task NavigateHistoryForTestingAsync(bool forward) => NavigateHistoryAsync(forward);
    internal Func<MarkdownLinkResolution, Task<string>>? MissingHeadingChoiceForTesting { get; set; }
    internal Task FollowRenderedLinkForTestingAsync(Uri request, string token, DocumentStructure rendered)
    {
        _previewLinkToken = token;
        _previewLinkStructure = rendered;
        return HandlePreviewLinkAsync(request);
    }
}
