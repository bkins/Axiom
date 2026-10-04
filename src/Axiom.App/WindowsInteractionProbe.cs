using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Axiom.Core;

namespace Axiom.App;

internal static partial class WindowsInteractionProbe
{
    internal static string? Root { get; private set; }
    internal static void Configure(string root)
    {
        Root=Path.GetFullPath(root);
        if(Directory.Exists(Root)) throw new IOException("Choose a fresh native interaction evidence root.");
        Directory.CreateDirectory(Root); Environment.SetEnvironmentVariable("AXIOM_STATE_ROOT",Path.Combine(Root,"state"));
    }
    internal static async Task StartAsync(MainWindow window)
    {
        try
        {
            await window.StartupCompleted;
            await window.ShowPersistentHelpForTestingAsync(null);
            await Task.Delay(1600); window.Activate(); window.UpdateLayout(); await Task.Delay(150);
            var help=window.PersistentHelpForTesting!;
            var topics=help.GetLogicalDescendants().OfType<ComboBox>().Single();
            var search=help.GetLogicalDescendants().OfType<TextBox>().Single();
            window.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent,(_,args)=>File.AppendAllText(Path.Combine(Root!,"input.txt"),"Pointer: "+args.Source+" "+(args.Source as Control)?.Name+" at "+args.GetPosition(window)+"\n"),Avalonia.Interactivity.RoutingStrategies.Tunnel,handledEventsToo:true);
            using var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width,(int)window.ClientSize.Height),new Vector(96,96));
            bitmap.Render(window); bitmap.Save(Path.Combine(Root!,"help-native-input-surface.png"),Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            await File.WriteAllTextAsync(Path.Combine(Root!,"geometry.json"),JsonSerializer.Serialize(new
            {
                MainWindow=window.TryGetPlatformHandle()!.Handle.ToInt64()
              , SearchPointerTarget=NativeProbeInput.PointerTarget(search).ToInt64()
              , TopicsPointerTarget=NativeProbeInput.PointerTarget(topics).ToInt64()
              , SearchBounds=search.Bounds.ToString()
              , TopicsBounds=topics.Bounds.ToString()
              , NativeHelpBounds=help.PreviewForTesting.Bounds.ToString()
              , SearchTargetDescription=NativeProbeInput.Describe(search)
              , TopicsTargetDescription=NativeProbeInput.Describe(topics)
              , NativeHelpScreen=help.PreviewForTesting.PointToScreen(new Point(0,0)).ToString()
              , SearchScreen=search.PointToScreen(new Point(0,0)).ToString()
              , TopicsScreen=topics.PointToScreen(new Point(0,0)).ToString()
            },new JsonSerializerOptions { WriteIndented=true }));
            window.FindControl<AvaloniaEdit.TextEditor>("Editor")!.TextArea.Focus();
            await NativeProbeInput.ClickAsync(search); await Task.Delay(100); NativeProbeInput.Type(window,"metadata"); await Task.Delay(180);
            for(var attempt=0;attempt<30 && search.Text!="metadata";attempt++) await Task.Delay(100);
            await File.AppendAllTextAsync(Path.Combine(Root!,"input.txt"),"Focus: "+window.FocusManager?.GetFocusedElement()+" searchFocused "+search.IsKeyboardFocusWithin+" text "+search.Text+"\n");
            Require(search.Text=="metadata","Real native click/WM_CHAR did not type into Help search. Actual text: "+search.Text);
            await NativeProbeInput.ClickAsync(topics); await Task.Delay(180);
            bitmap.Render(window); bitmap.Save(Path.Combine(Root!,"help-native-input-surface.png"),Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            Require(topics.IsDropDownOpen,"Native click did not open Help topics.");
            topics.IsDropDownOpen=false;
            await VerifyWorkspaceAsync(window);
            await File.WriteAllTextAsync(Path.Combine(Root!,"result.json"),JsonSerializer.Serialize(new
            {
                Version=window.BuildIdentityForTesting.DisplayText
              , NativeHelpClickTypingAndDropdown=true
              , CompactTitleAndCaptionLayout=true
              , InactiveTabContrastAndReservedScrollbar=true
              , NativeFileAndFolderDragReviewedMove=true
              , ActiveNestedFileSelectedAfterRefresh=true
              , NativeExplorerFileSelectionWithSpacesAndComma=true
              , SourceUndoRecoveryPreserved=true
              , HumanChecks="Physical interaction comfort, multiple DPI, screen readers, themes and final acceptance remain Ben's checks. Native probe uses Windows mouse and Unicode input, not direct control text assignment."
            },new JsonSerializerOptions { WriteIndented=true }));
        }
        catch(Exception exception) { await File.WriteAllTextAsync(Path.Combine(Root!,"error.txt"),exception.ToString()); }
        finally { window.UnsavedChoiceForTesting=_=>Task.FromResult("Discard"); window.Close(); }
    }
    private static void Require(bool value,string message) { if(!value) throw new IOException(message); }
}
