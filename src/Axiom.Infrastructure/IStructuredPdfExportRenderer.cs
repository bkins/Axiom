using Axiom.Core;

namespace Axiom.Infrastructure;

public interface IStructuredPdfExportRenderer
{
    Task<PdfRenderResult> RenderAsync(ExportDocument document
                                  , ExportSnapshot snapshot
                                  , PdfPageSize pageSize
                                  , CancellationToken cancellationToken);
}
