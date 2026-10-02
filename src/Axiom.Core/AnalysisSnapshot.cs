using System.Security.Cryptography;
using System.Text;

namespace Axiom.Core;

public sealed record AnalysisSnapshot(string ReferenceKey, string SourceHash, string Text)
{
    public static AnalysisSnapshot Capture(string referenceKey, string text)
        => new(referenceKey, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))), text);

    public bool Matches(string currentText) => SourceHash == Capture(ReferenceKey, currentText).SourceHash;
}
