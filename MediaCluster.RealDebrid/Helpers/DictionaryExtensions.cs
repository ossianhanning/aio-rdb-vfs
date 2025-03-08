namespace MediaCluster.RealDebrid.Helpers;

public static class DictionaryExtensions
{
    public static (List<(TKey Key, TValue OldValue, TValue NewValue)> ChangedValues, List<TKey> NewKeys, List<TKey> DisappearedKeys) FindDifferences<TKey, TValue>(
        this Dictionary<TKey, TValue> oldDict, 
        Dictionary<TKey, TValue> newDict, 
        Func<TValue, TValue, bool>? valueComparer = null) where TKey : notnull
    {
        valueComparer ??= EqualityComparer<TValue>.Default.Equals;

        var changedValues = oldDict.Join(newDict, kvp => kvp.Key, kvp => kvp.Key, (oldKvp, newKvp) => new { oldKvp.Key, OldValue = oldKvp.Value, NewValue = newKvp.Value })
            .Where(x => !valueComparer(x.OldValue, x.NewValue))
            .Select(x => (x.Key, x.OldValue, x.NewValue))
            .ToList();

        var newKeys = newDict.Keys.Except(oldDict.Keys).ToList();

        var disappearedKeys = oldDict.Keys.Except(newDict.Keys).ToList();

        return (changedValues, newKeys, disappearedKeys);
    }
}