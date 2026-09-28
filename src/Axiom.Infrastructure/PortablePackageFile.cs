namespace Axiom.Infrastructure;

public sealed record PortablePackageFile(string RelativePath, long Length, string Sha256);
