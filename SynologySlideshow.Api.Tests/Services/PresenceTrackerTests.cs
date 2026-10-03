using SynologySlideshow.Api.Services;
using Xunit;

namespace SynologySlideshow.Api.Tests.Services;

public class PresenceTrackerTests
{
    [Fact]
    public void JoiningAChannelCountsAsAViewerOfIt()
    {
        var tracker = new PresenceTracker();

        tracker.OnJoinedChannel("B", channelId: 1);

        Assert.Equal(1, tracker.GetViewerCount(1));
    }

    [Fact]
    public void LeavingAChannelDropsItsViewerCount()
    {
        var tracker = new PresenceTracker();
        tracker.OnJoinedChannel("A", channelId: 1);

        tracker.OnLeftChannel("A", channelId: 1);

        Assert.Equal(0, tracker.GetViewerCount(1));
    }

    [Fact]
    public void DisconnectingWhileJoinedRemovesFromTheChannelToo()
    {
        var tracker = new PresenceTracker();
        tracker.OnJoinedChannel("A", channelId: 1);

        tracker.OnDisconnected("A");

        Assert.Equal(0, tracker.GetViewerCount(1));
    }

    [Fact]
    public void MultipleViewersOfTheSameChannelAreAllCounted()
    {
        var tracker = new PresenceTracker();
        tracker.OnJoinedChannel("A", channelId: 1);
        tracker.OnJoinedChannel("B", channelId: 1);

        Assert.Equal(2, tracker.GetViewerCount(1));
    }

    [Fact]
    public void JoiningTheSameChannelTwiceDoesNotDoubleCountTheViewer()
    {
        var tracker = new PresenceTracker();

        tracker.OnJoinedChannel("A", channelId: 1);
        tracker.OnJoinedChannel("A", channelId: 1); // e.g. a retried JoinChannel call on the same connection

        Assert.Equal(1, tracker.GetViewerCount(1));
    }

    [Fact]
    public void JoiningADifferentChannelMovesTheViewerWithoutAnExplicitLeave()
    {
        var tracker = new PresenceTracker();

        tracker.OnJoinedChannel("A", channelId: 1);
        tracker.OnJoinedChannel("A", channelId: 2); // no OnLeftChannel(1) in between

        Assert.Equal(0, tracker.GetViewerCount(1));
        Assert.Equal(1, tracker.GetViewerCount(2));
    }

    [Fact]
    public void JoiningAFreshChannelReturnsNoPreviousChannel()
    {
        var tracker = new PresenceTracker();

        var previous = tracker.OnJoinedChannel("A", channelId: 1);

        Assert.Null(previous);
    }

    [Fact]
    public void JoiningTheSameChannelAgainReturnsNoPreviousChannel()
    {
        var tracker = new PresenceTracker();
        tracker.OnJoinedChannel("A", channelId: 1);

        var previous = tracker.OnJoinedChannel("A", channelId: 1);

        Assert.Null(previous);
    }

    [Fact]
    public void SwitchingChannelsReturnsThePreviousChannelId()
    {
        var tracker = new PresenceTracker();
        tracker.OnJoinedChannel("A", channelId: 1);

        var previous = tracker.OnJoinedChannel("A", channelId: 2);

        Assert.Equal(1, previous);
    }

    [Fact]
    public void DisconnectingAnUntrackedConnectionReturnsNull()
    {
        var tracker = new PresenceTracker();

        var left = tracker.OnDisconnected("never-joined");

        Assert.Null(left);
    }

    [Fact]
    public void DisconnectingAJoinedConnectionReturnsItsChannelId()
    {
        var tracker = new PresenceTracker();
        tracker.OnJoinedChannel("A", channelId: 1);

        var left = tracker.OnDisconnected("A");

        Assert.Equal(1, left);
    }

    [Fact]
    public void LeavingWithAMismatchedChannelIdDoesNotCorruptAnyCount()
    {
        var tracker = new PresenceTracker();
        tracker.OnJoinedChannel("A", channelId: 1);
        tracker.OnJoinedChannel("B", channelId: 2);

        tracker.OnLeftChannel("B", channelId: 1); // B is actually on channel 2

        Assert.Equal(1, tracker.GetViewerCount(1));
        Assert.Equal(1, tracker.GetViewerCount(2));

        // B's real mapping survived, so a later disconnect still decrements channel 2.
        tracker.OnDisconnected("B");
        Assert.Equal(0, tracker.GetViewerCount(2));
        Assert.Equal(1, tracker.GetViewerCount(1));
    }

    [Fact]
    public void RemovingAChannelDropsItsViewerCount()
    {
        var tracker = new PresenceTracker();
        tracker.OnJoinedChannel("A", channelId: 1);
        tracker.OnJoinedChannel("B", channelId: 1);
        tracker.OnJoinedChannel("C", channelId: 2);

        tracker.RemoveChannel(1);

        Assert.Equal(0, tracker.GetViewerCount(1));
        Assert.Equal(1, tracker.GetViewerCount(2));
    }

    [Fact]
    public void ViewersOfARemovedChannelCanDisconnectOrRejoinWithoutCorruptingCounts()
    {
        var tracker = new PresenceTracker();
        tracker.OnJoinedChannel("A", channelId: 1);
        tracker.OnJoinedChannel("B", channelId: 1);
        tracker.RemoveChannel(1);

        tracker.OnDisconnected("A");
        tracker.OnJoinedChannel("B", channelId: 2);

        Assert.Equal(0, tracker.GetViewerCount(1));
        Assert.Equal(1, tracker.GetViewerCount(2));
    }
}
