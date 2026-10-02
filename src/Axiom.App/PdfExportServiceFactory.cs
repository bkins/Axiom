using Axiom.Infrastructure;

namespace Axiom.App;

internal static class PdfExportServiceFactory
{
    internal static DocumentExportService Create(string stateRoot) => OperatingSystem.IsLinux()
        ? new(structuredPdfRenderer: new PortablePdfExportRenderer())
        : new(new NativePdfExportRenderer(stateRoot));
}
