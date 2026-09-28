using Avalonia;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--recovery-probe") return RunRecoveryProbe(args);
        if (args.FirstOrDefault() == "--verify-recovery") return VerifyRecovery(args);
        if (args.FirstOrDefault() is "--verify-package" or "--verify-and-restart-package") return PortablePackageProbe.Run(args);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

    private static int RunRecoveryProbe(string[] args)
    {
        if (args.Length != 4) return 2;
        var paths = new AppDataPaths(args[1]);
        var store = new SqliteStateStore(paths);
        store.InitializeAsync().GetAwaiter().GetResult();
        File.WriteAllText(args[2], "ready");
        var sequence = 0;
        var lastProtectedUtc = DateTimeOffset.MinValue;
        while (true)
        {
            sequence++;
            File.WriteAllText(args[3], sequence.ToString());
            if (DateTimeOffset.UtcNow - lastProtectedUtc >= TimeSpan.FromMilliseconds(300))
            {
                store.SaveDraftAsync(new RecoveryDraft("probe", null, sequence.ToString(), DateTimeOffset.UtcNow, null, true)).GetAwaiter().GetResult();
                lastProtectedUtc = DateTimeOffset.UtcNow;
            }
            Thread.Sleep(50);
        }
    }

    private static int VerifyRecovery(string[] args)
    {
        if (args.Length != 3) return 2;
        var store = new SqliteStateStore(new AppDataPaths(args[1]));
        store.InitializeAsync().GetAwaiter().GetResult();
        var draft = store.GetRecoverableDraftsAsync().GetAwaiter().GetResult().FirstOrDefault(item => item.DraftKey == "probe");
        if (draft is null) return 3;
        File.WriteAllText(args[2], draft.Text);
        return 0;
    }
}
