using Axiom.Infrastructure;

namespace Axiom.Infrastructure.Tests;

public sealed class GitCommandRunnerTests
{
    [Fact]
    public async Task MissingGitReportsFeatureAvailabilityWithoutChangingMachineConfiguration()
    {
        var executable = Path.Combine(Path.GetTempPath(), "AxiomMissingGit-" + Guid.NewGuid().ToString("N") + ".exe");
        var error = await Assert.ThrowsAsync<IOException>(() => new GitCommandRunner(executable).RunAsync(Path.GetTempPath(), new[] { "--version" }));
        Assert.Contains("Git is unavailable", error.Message);
        Assert.Contains("editing and recovery remain available", error.Message);
    }
}
