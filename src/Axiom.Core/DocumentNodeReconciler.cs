namespace Axiom.Core;

internal static class DocumentNodeReconciler
{
    public static Guid[] Match<T>(IReadOnlyList<T> previous
                               , IReadOnlyList<T> current
                               , Func<T, string> signature
                               , Func<T, Guid> identity
                               , Func<T, T, bool>? canReplace = null)
    {
        var result = current.Select(_ => Guid.NewGuid()).ToArray();
        var matches = new Dictionary<int, int>();
        var oldGroups = Enumerable.Range(0, previous.Count).GroupBy(index => signature(previous[index]))
                                  .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var newGroups = Enumerable.Range(0, current.Count).GroupBy(index => signature(current[index]));
        foreach (var group in newGroups)
        {
            var newIndexes = group.ToArray();
            if (!oldGroups.TryGetValue(group.Key, out var oldIndexes) || oldIndexes.Length != newIndexes.Length)
                continue;
            // A changed duplicate count is ambiguous; do not guess which node survived.
            for (var index = 0; index < newIndexes.Length; index++)
            {
                result[newIndexes[index]] = identity(previous[oldIndexes[index]]);
                matches.Add(newIndexes[index], oldIndexes[index]);
            }
        }

        if (canReplace is null) return result;
        var boundaries = matches.OrderBy(pair => pair.Key)
                                .Prepend(new KeyValuePair<int, int>(-1, -1))
                                .Append(new KeyValuePair<int, int>(current.Count, previous.Count))
                                .ToArray();
        for (var index = 1; index < boundaries.Length; index++)
        {
            var before = boundaries[index - 1];
            var after = boundaries[index];
            if (after.Key - before.Key != 2 || after.Value - before.Value != 2) continue;
            var currentIndex = before.Key + 1;
            var previousIndex = before.Value + 1;
            if (!canReplace(previous[previousIndex], current[currentIndex])) continue;
            result[currentIndex] = identity(previous[previousIndex]);
        }
        return result;
    }
}
