using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Axiom.Core;

namespace Axiom.App;

internal static partial class WindowsInteractionProbe
{
    private static async Task VerifyWorkspaceAsync(MainWindow window)
    {
        window.FindControl<Border>("HelpPanelHost")!.IsVisible=false;
        var workspace=Path.Combine(Root!,"workspace with spaces, comma");
        Directory.CreateDirectory(Path.Combine(workspace,"nested","deep"));
        Directory.CreateDirectory(Path.Combine(workspace,"target"));
        var source=Path.Combine(workspace,"nested","deep","source.md");
        await File.WriteAllTextAsync(source,"# Source\n\nSaved bytes");
        await File.WriteAllTextAsync(Path.Combine(workspace,"target","anchor.md"),"Target");
        await File.WriteAllTextAsync(Path.Combine(workspace,"moving.md"),"Move me");
        await window.SwitchWorkspaceAsync(workspace); await window.OpenDocumentAsync(source);
        var editor=window.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
        editor.Document.Insert(editor.Text.Length,"\nUnsaved retained");
        await window.ProtectIfNeededForTestingAsync();
        var dirty=editor.Text;
        var tree=window.FindControl<TreeView>("FileTree")!;
        tree.SelectedItem=window.ExplorerNodesForTesting.Single(node=>node.Name=="target");
        window.FilterExplorerForTesting(""); await Task.Delay(300);
        Require(tree.SelectedItem is WorkspaceExplorerNode {IsDocument:true} selected && selected.RelativePath==Path.GetRelativePath(workspace,source),"Refresh retained stale selection instead of active file.");
        var fileItem=FindItem(tree,Path.GetRelativePath(workspace,source));
        Require(fileItem.IsVisible && fileItem.Bounds.Height>0,"Nested active file was not realized.");
        for(var index=0;index<7;index++)
        {
            var path=Path.Combine(workspace,"long document header "+index+".md"); await File.WriteAllTextAsync(path,"# Tab "+index); await window.OpenDocumentAsync(path);
        }
        await Task.Delay(450); window.UpdateLayout();
        var tabs=window.FindControl<ListBox>("DocumentTabs")!;
        var scroll=tabs.GetVisualDescendants().OfType<ScrollBar>().First(bar=>bar.Orientation==Avalonia.Layout.Orientation.Horizontal && bar.IsVisible);
        var tab=tabs.GetVisualDescendants().OfType<ListBoxItem>().First(item=>item.IsVisible && item.Bounds.Width>0);
        var scrollTop=scroll.TranslatePoint(new Point(0,0),window)!.Value.Y;
        var tabBottom=tab.TranslatePoint(new Point(0,tab.Bounds.Height),window)!.Value.Y;
        Require(scrollTop>=tabBottom-1,"Horizontal scrollbar overlays tab content: "+scrollTop+" < "+tabBottom);
        Require(tab.Background is not null,"Inactive tab has no distinct background.");
        Require(window.WindowDecorations==WindowDecorations.BorderOnly && window.Title?.Contains("long document header")==true,"Compact chrome failed to retain OS title while removing native text painting.");
        var title=window.FindControl<Border>("TitleDragRegion")!;
        var menu=window.FindControl<Button>("FileCommandsButton")!;
        Require(title.Bounds.X>=menu.Bounds.Right,"Custom title overlaps command content.");
        Capture(window,"tabs-and-compact-title-surface.png");
        await window.SelectTabForTestingAsync(window.TabsForTesting.Single(tab=>tab.Session.Path==source)); await Task.Delay(200);
        var drag=NativeProbeInput.DragAsync(FindItem(tree,"moving.md"),FindItem(tree,"target"));
        await drag; await AnswerAsync(window,"Review move and references","Move and repair"); await window.LastExplorerMove.WaitAsync(TimeSpan.FromSeconds(10));
        Require(File.Exists(Path.Combine(workspace,"target","moving.md")) && !File.Exists(Path.Combine(workspace,"moving.md")),"Native file drag did not move into the folder.");
        await Task.Delay(400);
        window.UnsavedChoiceForTesting=_=>Task.FromResult("Cancel");
        var cancel=NativeProbeInput.DragAsync(FindItem(tree,"nested"),FindItem(tree,"target"));
        await cancel; await AnswerAsync(window,"Save or resolve affected documents","OK"); await window.LastExplorerMove.WaitAsync(TimeSpan.FromSeconds(10));
        Require(File.Exists(source) && editor.Text==dirty,"Cancelled dirty descendant folder drag changed source.");
        window.UnsavedChoiceForTesting=_=>Task.FromResult("Discard");
        window.SetEditorTextForTesting("# Source\n\nSaved bytes");
        var folder=NativeProbeInput.DragAsync(FindItem(tree,"nested"),FindItem(tree,"target"));
        await folder; await AnswerAsync(window,"Review move and references","Move and repair"); await window.LastExplorerMove.WaitAsync(TimeSpan.FromSeconds(10));
        var moved=Path.Combine(workspace,"target","nested","deep","source.md");
        Require(File.Exists(moved) && await File.ReadAllTextAsync(moved)=="# Source\n\nSaved bytes","Reviewed native folder drag lost saved source.");
        await VerifyExplorerSelectionAsync(window,workspace);
        window.Activate();
        Require(editor.Document.UndoStack.CanUndo || !window.TabsForTesting.Single(tab=>tab.Session.Path==moved).Session.IsDirty,"Move failed dirty/Undo contract.");
    }
    private static TreeViewItem FindItem(TreeView tree,string relative)
        => tree.GetVisualDescendants().OfType<TreeViewItem>().Single(item=>item.DataContext is WorkspaceExplorerNode {IsGroup:false} node && node.Heading is null && node.RelativePath==relative);
    private static async Task AnswerAsync(Window owner,string title,string button)
    {
        for(var attempt=0;attempt<100;attempt++)
        {
            if(owner.OwnedWindows.FirstOrDefault(dialog=>dialog.Title==title) is { } dialog)
            {
                dialog.Activate(); dialog.UpdateLayout(); await Task.Delay(200);
                var candidates=dialog.GetLogicalDescendants().OfType<Button>().ToArray();
                await File.WriteAllTextAsync(Path.Combine(Root!,"last-dialog.json"),JsonSerializer.Serialize(new {title,Buttons=candidates.Select(item=>item.Content?.ToString()).ToArray()}));
                var choice=candidates.SingleOrDefault(item=>Equals(item.Content,button)) ?? candidates.Single(item=>Equals(item.Content,"Continue") || Equals(item.Content,"Apply") || Equals(item.Content,"Move"));
                await NativeProbeInput.ClickAsync(choice); return;
            }
            await Task.Delay(80);
        }
        throw new IOException("Native move review missing: "+title);
    }
    private static async Task VerifyExplorerSelectionAsync(MainWindow window,string workspace)
    {
        if(!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var path=Path.Combine(workspace,"target","file with spaces, comma.md"); await File.WriteAllTextAsync(path,"Explorer bytes");
        foreach (var requested in new[] { path, Path.Combine(workspace, "target"), workspace })
            await VerifyExplorerItemAsync(window, workspace, requested);
    }

    private static async Task VerifyExplorerItemAsync(MainWindow window, string workspace, string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var result = path == workspace
            ? await window.ExecuteCommandAsync("workspace.showFolderInExplorer")
            : await window.ExecuteExplorerCommandForTestingAsync("workspace.showInExplorer",Path.GetRelativePath(workspace,path), Directory.Exists(path));
        Require(result.Outcome==CommandOutcome.Completed,"Explorer shell call failed: "+result.Message+" "+result.Error);
        dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        for(var attempt=0;attempt<50;attempt++)
        {
            foreach(dynamic explorer in shell.Windows())
            {
                try
                {
                    if(!string.Equals((string)explorer.Document.Folder.Self.Path,Path.GetDirectoryName(path),StringComparison.OrdinalIgnoreCase)) continue;
                    foreach(dynamic selected in explorer.Document.SelectedItems())
                        if(string.Equals((string)selected.Path,path,StringComparison.OrdinalIgnoreCase)) { explorer.Quit(); return; }
                }
                catch(System.Runtime.InteropServices.COMException) { }
            }
            await Task.Delay(100);
        }
        throw new IOException("Actual Explorer did not select the requested fixture path.");
    }
    private static void Capture(MainWindow window,string name)
    {
        window.UpdateLayout();
        using var image=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width,(int)window.ClientSize.Height),new Vector(96,96));
        image.Render(window); image.Save(Path.Combine(Root!,name),Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
