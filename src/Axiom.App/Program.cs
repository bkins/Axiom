using Avalonia;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--template-asset-probe" && args.Length == 2) TemplateAssetProbe.Configure(args[1]);
        if (args.FirstOrDefault() == "--pdf-export-probe" && args.Length == 2) PdfExportProbe.Configure(args[1]);
        if (args.FirstOrDefault() == "--everyday-use-probe" && args.Length == 2) EverydayUseProbe.Configure(args[1]);
        if (args.FirstOrDefault() == "--benchmark-link-index" && args.Length == 3) return LinkIndexBenchmarkProbe.Run(args[1], args[2]);
        if (args.FirstOrDefault() == "--benchmark-classification" && args.Length == 3) return ClassificationBenchmarkProbe.Run(args[1], args[2]);
        if (args.FirstOrDefault() == "--recovery-probe") return RunRecoveryProbe(args);
        if (args.FirstOrDefault() == "--verify-recovery") return VerifyRecovery(args);
        if (args.FirstOrDefault() is "--verify-package" or "--verify-and-restart-package") return PortablePackageProbe.Run(args);
        if (args.FirstOrDefault() == "--verify-multi-buffer-recovery" && args.Length == 3)
            return MultiBufferRecoveryProbe.Verify(args[1], args[2]);
        if (args.FirstOrDefault() == "--multi-buffer-recovery-probe" && args.Length is 2 or 3)
            MultiBufferRecoveryProbe.Configure(args[1], args.Length == 3 && args[2] == "--reading-mode");
        if (args.FirstOrDefault() is "--connected-documents-probe" or "--verify-connected-documents" && args.Length == 2)
            ConnectedDocumentsProbe.Configure(args[1], args[0] == "--verify-connected-documents");
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
