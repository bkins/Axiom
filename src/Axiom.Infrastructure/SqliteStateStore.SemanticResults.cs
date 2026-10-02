using System.Text.Json;
using Axiom.Core;

namespace Axiom.Infrastructure;

public sealed partial class SqliteStateStore : ISemanticDocumentResultStore
{
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM AppSettings WHERE SettingKey LIKE 'SemanticResult:%';";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    public async Task<SemanticDocumentResult?> ReadAsync(string documentKey, string providerId, string providerVersion, string contentHash, long revision, CancellationToken cancellationToken = default)
    {
        var json = await ReadSettingAsync(SemanticKey(documentKey, providerId, providerVersion, contentHash, revision), cancellationToken);
        if (json is null) return null;
        var result = JsonSerializer.Deserialize<SemanticDocumentResult>(json) ?? throw new InvalidDataException("The semantic cache entry is invalid.");
        if (result.DocumentKey != documentKey || result.ProviderId != providerId || result.ProviderVersion != providerVersion || result.ContentHash != contentHash || result.Revision != revision)
            throw new InvalidDataException("The semantic cache provenance does not match the request.");
        return result;
    }

    public async Task WriteAsync(SemanticDocumentResult result, CancellationToken cancellationToken = default)
    {
        if (result.Revision < 0 || string.IsNullOrWhiteSpace(result.DocumentKey) || string.IsNullOrWhiteSpace(result.ProviderId) || string.IsNullOrWhiteSpace(result.ProviderVersion)
            || result.ContentHash.Length != 64 || !result.ContentHash.All(Uri.IsHexDigit) || result.ResultJson.Length > 2 * 1024 * 1024)
            throw new ArgumentException("The semantic result has invalid provenance or exceeds the cache entry limit.");
        using var validated = JsonDocument.Parse(result.ResultJson);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await WriteSettingAsync(connection, transaction, SemanticKey(result.DocumentKey, result.ProviderId, result.ProviderVersion, result.ContentHash, result.Revision), JsonSerializer.Serialize(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string SemanticKey(string documentKey, string providerId, string providerVersion, string contentHash, long revision) =>
        "SemanticResult:" + WorkspaceLinkIndexService.Hash(JsonSerializer.Serialize(new object[] { documentKey, providerId, providerVersion, contentHash, revision }));
}
