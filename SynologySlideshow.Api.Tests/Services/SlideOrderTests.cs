using SynologySlideshow.Api.Services;
using Xunit;

namespace SynologySlideshow.Api.Tests.Services;

public class SlideOrderTests
{
    private static int Id(int x) => x;

    [Fact]
    public void ShuffleKeepsEveryItemExactlyOnce()
    {
        var items = Enumerable.Range(1, 200).ToArray();

        SlideOrder.Shuffle(items, new Random(1));

        Assert.Equal(Enumerable.Range(1, 200), items.OrderBy(x => x));
    }

    [Fact]
    public void MergeWithNoPreviousOrderShufflesEverything()
    {
        var fresh = Enumerable.Range(1, 50).ToArray();

        var merged = SlideOrder.Merge(Array.Empty<int>(), fresh, Id, new Random(1));

        Assert.Equal(fresh, merged.OrderBy(x => x));
        Assert.NotEqual(fresh, merged);
        Assert.Equal(Enumerable.Range(1, 50), fresh); // input untouched
    }

    [Fact]
    public void MergeKeepsTheExistingRelativeOrder()
    {
        var previous = new[] { 5, 3, 9, 1, 7 };

        var merged = SlideOrder.Merge(previous, new[] { 1, 3, 5, 7, 9 }, Id, new Random(1));

        Assert.Equal(previous, merged);
    }

    [Fact]
    public void MergeDropsRemovedItemsAndKeepsTheRestInOrder()
    {
        var merged = SlideOrder.Merge(new[] { 5, 3, 9, 1, 7 }, new[] { 1, 5, 9 }, Id, new Random(1));

        Assert.Equal(new[] { 5, 9, 1 }, merged);
    }

    [Fact]
    public void MergeInsertsNewItemsWithoutDisturbingTheOrderOfExistingOnes()
    {
        var previous = new[] { 5, 3, 9, 1, 7 };
        var fresh = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        var merged = SlideOrder.Merge(previous, fresh, Id, new Random(1));

        Assert.Equal(fresh, merged.OrderBy(x => x));
        Assert.Equal(previous, merged.Where(previous.Contains));
    }

    [Fact]
    public void MergeScattersNewItemsAcrossTheWholeOrderRatherThanAppendingThem()
    {
        var previous = Enumerable.Range(1, 100).ToArray();
        var fresh = Enumerable.Range(1, 200).ToArray();

        var merged = SlideOrder.Merge(previous, fresh, Id, new Random(1));

        var positionsOfNewItems = merged.Select((id, index) => (id, index)).Where(p => p.id > 100).Select(p => p.index).ToArray();
        Assert.True(positionsOfNewItems.Min() < 50, "some new items should land in the first part of the order");
        Assert.True(positionsOfNewItems.Max() > 150, "some new items should land in the last part of the order");
    }

    [Fact]
    public void ReshuffleNeverOpensWithTheAvoidedItem()
    {
        var items = Enumerable.Range(1, 5).ToArray();

        for (var seed = 0; seed < 500; seed++)
        {
            var reshuffled = SlideOrder.Reshuffle(items, Id, avoidFirstId: 3, new Random(seed));

            Assert.NotEqual(3, reshuffled[0]);
            Assert.Equal(items, reshuffled.OrderBy(x => x));
        }
    }

    [Fact]
    public void ReshuffleOfASingleItemJustReturnsIt()
    {
        var reshuffled = SlideOrder.Reshuffle(new[] { 7 }, Id, avoidFirstId: 7, new Random(1));

        Assert.Equal(new[] { 7 }, reshuffled);
    }

    // Walking the album one photo per tick and reshuffling only when a pass finishes must show
    // every photo the same number of times, however many refreshes happen along the way.
    [Fact]
    public void RefreshesBetweenTicksDoNotSkewHowOftenPhotosAreShown()
    {
        var random = new Random(42);
        var photos = Enumerable.Range(1, 1000).ToArray();
        var order = SlideOrder.Merge(Array.Empty<int>(), photos, Id, random);
        var views = photos.ToDictionary(p => p, _ => 0);
        var position = -1;

        for (var tick = 0; tick < 5000; tick++)
        {
            if (tick % 300 == 0) order = SlideOrder.Merge(order, photos, Id, random); // a refresh

            position++;
            if (position == order.Length)
            {
                order = SlideOrder.Reshuffle(order, Id, order[^1], random);
                position = 0;
            }
            views[order[position]]++;
        }

        // 5000 ticks over 1000 photos: 5 full passes, so every photo is shown exactly 5 times.
        Assert.All(views.Values, count => Assert.Equal(5, count));
    }
}
