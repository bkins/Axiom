using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Axiom.App;

[assembly: AvaloniaTestApplication(typeof(Axiom.App.Tests.HeadlessApp))]

namespace Axiom.App.Tests;

public sealed class HeadlessApp : Application
{
    public override void Initialize()
    {
        // Use actual desktop control templates for focus, popup, and pointer tests,
        // without initializing native WebView styling in the headless host.
        Styles.Add(new FluentTheme());
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Axiom.App.Tests/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml")
        });
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<HeadlessApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
