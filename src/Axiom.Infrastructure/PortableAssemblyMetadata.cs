using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Axiom.Infrastructure;

public static class PortableAssemblyMetadata
{
    public static int ReadSchemaVersion(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var portable = new PEReader(stream);
        var metadata = portable.GetMetadataReader();
        foreach (var typeHandle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(typeHandle);
            if (metadata.GetString(type.Namespace) != "Axiom.Infrastructure" || metadata.GetString(type.Name) != "SqliteStateStore") continue;
            foreach (var fieldHandle in type.GetFields())
            {
                var field = metadata.GetFieldDefinition(fieldHandle);
                if (metadata.GetString(field.Name) != "CurrentSchemaVersion" || field.GetDefaultValue().IsNil) continue;
                var constant = metadata.GetConstant(field.GetDefaultValue());
                if (constant.TypeCode == ConstantTypeCode.Int32) return metadata.GetBlobReader(constant.Value).ReadInt32();
            }
        }
        throw new IOException("The payload does not identify its supported state schema.");
    }
}
