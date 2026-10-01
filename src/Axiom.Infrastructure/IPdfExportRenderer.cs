using Axiom.Core;

namespace Axiom.Infrastructure;

public interface IPdfExportRenderer
{
    Task<PdfRenderResult> RenderAsync(string html, PdfPageSize pageSize, CancellationToken cancellationToken);
}
