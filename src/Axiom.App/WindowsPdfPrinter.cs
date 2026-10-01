using System.Runtime.Versioning;
using Avalonia.Platform;
using Axiom.Core;
using Microsoft.Web.WebView2.Core;

namespace Axiom.App;

[SupportedOSPlatform("windows")]
internal static class WindowsPdfPrinter
{
    public static Task<Stream> PrintAsync(IWindowsWebView2PlatformHandle handle, PdfPageSize pageSize)
    {
        var browser = CoreWebView2.CreateFromComICoreWebView2(handle.CoreWebView2);
        browser.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Light;
        var settings = browser.Environment.CreatePrintSettings();
        settings.Orientation = CoreWebView2PrintOrientation.Portrait;
        settings.PageWidth = pageSize == PdfPageSize.A4 ? 210 / 25.4 : 8.5;
        settings.PageHeight = pageSize == PdfPageSize.A4 ? 297 / 25.4 : 11;
        settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = 0;
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        return browser.PrintToPdfStreamAsync(settings);
    }
}
