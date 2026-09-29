using Avalonia.Headless.XUnit;
using Axiom.Infrastructure;
using Xunit;

namespace Axiom.App.Tests;

public sealed partial class MainWindowTests
{
    [AvaloniaFact]
    public async Task AboutUsesAuthoritativeBuildAndInjectedStateLocation()
    {
        var root = Path.Combine(Path.GetTempPath(), "AxiomAboutUi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var window = CreateWindow(root);
            var text = await window.GetAboutInformationAsync();
            Assert.Contains(window.BuildIdentityForTesting.InformationalVersion, text);
            Assert.Contains(Path.Combine(root, "state"), text);
            Assert.Contains($"Supported state schema: {SqliteStateStore.CurrentSchemaVersion}", text);
            Assert.Contains("development build", text);
            Assert.True(window.PrimaryCommandsHaveAccessibleHelpForTesting);
            window.Close();
        }
        finally { Directory.Delete(root, true); }
    }
}
