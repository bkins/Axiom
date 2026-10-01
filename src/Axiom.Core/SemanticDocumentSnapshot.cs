namespace Axiom.Core;

public sealed record SemanticDocumentSnapshot(string DocumentKey
                                           , Guid? DocumentId
                                           , string ContentHash
                                           , long Revision
                                           , DocumentStructure Structure)
{
    public static SemanticDocumentSnapshot Create(string documentKey, long revision, DocumentStructure structure) =>
        new(documentKey, DocumentIdCodec.Find(structure.SourceText), WorkspaceLinkIndexService.Hash(structure.SourceText), revision, structure);
}
