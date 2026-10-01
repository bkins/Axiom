using Axiom.Core;

namespace Axiom.App;

internal sealed record HistoricalRestoreRequest(DocumentRevision Revision, string Text);
