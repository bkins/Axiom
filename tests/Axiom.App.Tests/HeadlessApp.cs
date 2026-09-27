using Avalonia;
using Avalonia.Headless;
using Axiom.App;

[assembly: AvaloniaTestApplication(typeof(Axiom.App.Tests.HeadlessApp))]

namespace Axiom.App.Tests;

public sealed class HeadlessApp : Application
{
    public override void Initialize()
    {
        // The production App XAML initializes native WebView styling. The headless
        // fixture needs only the logical Avalonia application host.
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<HeadlessApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
