namespace SynologySlideshow.Api.Services;

public static class SlideOrder
{
    public static void Shuffle<T>(T[] items, Random random)
    {
        // Fisher-Yates
        for (var i = items.Length; i > 1; i--)
        {
            var j = random.Next(i);
            (items[j], items[i - 1]) = (items[i - 1], items[j]);
        }
    }

    // Keeps the relative order of items that were already in the previous order, drops the ones
    // that are gone, and drops each new item into a random position. Reshuffling on every
    // refresh would throw away how far through the current pass a channel was, so large
    // libraries would never finish a pass and some photos would be shown far more than others.
    public static T[] Merge<T>(IReadOnlyList<int> previousIds, T[] fresh, Func<T, int> idOf, Random random)
    {
        if (previousIds.Count == 0)
        {
            var shuffled = (T[])fresh.Clone();
            Shuffle(shuffled, random);
            return shuffled;
        }

        var freshById = new Dictionary<int, T>(fresh.Length);
        foreach (var item in fresh) freshById[idOf(item)] = item;

        var result = new List<T>(fresh.Length);
        var placed = new HashSet<int>();
        foreach (var id in previousIds)
        {
            if (freshById.TryGetValue(id, out var item) && placed.Add(id)) result.Add(item);
        }

        foreach (var item in fresh)
        {
            if (placed.Add(idOf(item))) result.Insert(random.Next(result.Count + 1), item);
        }

        return result.ToArray();
    }

    // A fresh random order for the next pass, guaranteed not to open with the photo that
    // closed the previous pass (the same photo twice in a row looks like a glitch).
    public static T[] Reshuffle<T>(T[] items, Func<T, int> idOf, int avoidFirstId, Random random)
    {
        var shuffled = (T[])items.Clone();
        Shuffle(shuffled, random);
        if (shuffled.Length > 1 && idOf(shuffled[0]) == avoidFirstId)
        {
            var swapWith = random.Next(1, shuffled.Length);
            (shuffled[0], shuffled[swapWith]) = (shuffled[swapWith], shuffled[0]);
        }
        return shuffled;
    }
}
