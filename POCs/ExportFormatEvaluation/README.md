# Additional export fixture

Ben selected EPUB and OpenDocument Text on October 1, 2026. This console fixture now exercises the production `DocumentExportService`; it is not a second maintained exporter. `EpubEvaluationWriter.cs` is retained as the initial frozen experiment and excluded from compilation. Its original output and EPUBCheck report remain in `artifacts/ExportFormatEvaluation-01`.

Run from the source root with a **fresh** output directory:

```powershell
dotnet run --project POCs/ExportFormatEvaluation/ExportFormatEvaluation.csproj -c Release -- artifacts/AdditionalExports-01
```

The fixture writes disposable saved Markdown/image data, exports a different unsaved snapshot to both formats, and checks source bytes, identity exclusion, image deduplication and active-content exclusion. Existing output directories are refused.

Validate EPUB with the official W3C EPUBCheck distribution and ODT with `scripts/Verify-AdditionalExportEvidence.py` plus the official OASIS 1.3 content/manifest Relax NG schemas. Keep actual reader layout, picker interaction and human acceptance pending. The application menu and shared commands use the production service.
