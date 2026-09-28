namespace Axiom.Core;

public enum MarkdownLinkResolutionStatus
{
    Resolved
  , NoWorkspace
  , SourceNotSaved
  , InvalidPath
  , UnsupportedScheme
  , AbsolutePath
  , UnsupportedQuery
  , UnsupportedDocument
  , OutsideWorkspace
  , MissingDocument
  , MissingHeading
  , ExcludedPath
  , Unavailable
}
