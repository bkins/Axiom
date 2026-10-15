using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace Axiom.App;

internal static class TurnkeyCpProbe
{
    internal static string? Root { get; private set; }
    internal static void Configure(string root)
    {
        Root = Path.GetFullPath(root);
        if (Directory.Exists(Root)) throw new IOException("Choose fresh turn-key evidence.");
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT", Path.Combine(Root,"state"));
    }
    internal static async Task StartAsync(MainWindow main)
    {
        try
        {
            await main.StartupCompleted;
            var workspace=Path.Combine(Root!,"workspace"); Directory.CreateDirectory(workspace);
            var path=Path.Combine(workspace,"Trial.md"); const string saved="# Trial\nteh small bird.\n";
            await File.WriteAllTextAsync(path,saved); await main.SwitchWorkspaceAsync(workspace); await main.OpenDocumentAsync(path);
            main.FindControl<AvaloniaEdit.TextEditor>("Editor")!.Document.Insert(saved.Length,"Unsaved note.\n");
            var original=main.EditorTextForTesting;
            var operation=main.ExecuteCommandAsync("document.cpAnalysis");
            await Wait(()=>main.OwnedWindows.OfType<CpDocumentActionWindow>().Any());
            var actions=main.OwnedWindows.OfType<CpDocumentActionWindow>().Single();
            await Wait(()=>Status(actions).StartsWith("Analysis ready"),actions);
            Require(main.EditorTextForTesting==original,"Analysis changed source");
            actions.Close(); await operation;
            operation=main.ExecuteCommandAsync("document.cpEdits");
            await Wait(()=>main.OwnedWindows.OfType<CpDocumentActionWindow>().Any());
            actions=main.OwnedWindows.OfType<CpDocumentActionWindow>().Single();
            Control<TextBox>(actions,"CpEditRequest").Text="Correct only the typo teh to the. Preserve every other word, line, heading and document marker.";
            Click(actions,"CpActionSuggest");
            await Wait(()=>Control<Button>(actions,"CpActionPreview").IsEnabled,actions);
            Require(main.EditorTextForTesting==original,"Suggestion changed source before Apply");
            await File.WriteAllTextAsync(Path.Combine(Root!,"proposed.md"),Control<TextBox>(actions,"CpActionOutput").Text);
            Click(actions,"CpActionPreview");
            await Wait(()=>actions.OwnedWindows.OfType<LineDiffWindow>().Any());
            actions.OwnedWindows.OfType<LineDiffWindow>().Single().Close();
            await Wait(()=>Control<Button>(actions,"CpActionApply").IsEnabled);
            Click(actions,"CpActionApply");
            Require(main.EditorTextForTesting.Contains("the small bird") && main.EditorTextForTesting.Contains("Unsaved note."),"Proposal did not retain the expected source content");
            Require(await File.ReadAllTextAsync(path)==saved,"Apply saved the file");
            actions.Close(); await operation;
            main.UndoEditorForTesting(); Require(main.EditorTextForTesting==original,"One Undo did not restore pre-Apply text");
            operation=main.ExecuteCommandAsync("workspace.cpAnalysis");
            await Wait(()=>main.OwnedWindows.OfType<CpWorkspaceAnalysisWindow>().Any());
            var search=main.OwnedWindows.OfType<CpWorkspaceAnalysisWindow>().Single();
            Control<TextBox>(search,"CpSemanticQuery").Text="small bird";
            Click(search,"CpSemanticSearch");
            await Wait(()=>Control<ListBox>(search,"CpSemanticResults").ItemCount>0);
            search.Close(null); await operation;
            Require(main.EditorTextForTesting==original && await File.ReadAllTextAsync(path)==saved,"Search changed source or disk");
            await File.WriteAllTextAsync(Path.Combine(Root!,"result.json"),JsonSerializer.Serialize(new {Version=main.BuildIdentityForTesting.InformationalVersion,AutomaticProtectedCredentialConnection=true,AnalyzeCurrentUnsavedBuffer=true,NoClipboardOrManualEndpoint=true,ProposalPreviewBeforeApply=true,ActualApplyAndSingleUndo=true,DiskUnchanged=true,SearchWithoutBuildStep=true,SyntheticSourcesOnly=true,HumanEditQualityAccepted=false},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception error){await File.WriteAllTextAsync(Path.Combine(Root!,"error.txt"),error.ToString());}
        finally{foreach(var child in main.OwnedWindows.ToArray())child.Close();main.Close();}
    }
    private static T Control<T>(Window window,string name) where T:Control=>window.GetLogicalDescendants().OfType<T>().Single(control=>control.Name==name);
    private static string Status(Window window)=>Control<TextBlock>(window,"CpActionStatus").Text??"";
    private static void Click(Window window,string name)=>Control<Button>(window,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Require(bool condition,string message){if(!condition)throw new IOException(message);}
    private static async Task Wait(Func<bool> condition,Window? action=null)
    {
        var end=DateTime.UtcNow.AddSeconds(140);
        while(!condition())
        {
            if(action is not null && Control<Button>(action,"CpActionAnalyze").IsEnabled && !Status(action).StartsWith("Analyze or suggest") && !Status(action).StartsWith("Analysis ready") && !Status(action).StartsWith("Proposal ready")) throw new IOException(Status(action));
            if(DateTime.UtcNow>end)throw new TimeoutException("Turn-key native action timed out"); await Task.Delay(100);
        }
    }
}
