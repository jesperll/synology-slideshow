using SynologySlideshow.Api.Services;
using Xunit;

namespace SynologySlideshow.Api.Tests.Services;

public class PresenceTrackerTests
{
    [Fact]
    public void AnonymousCountEqualsTotalConnectionsMinusJoinedOnes()
    {
        var tracker = new PresenceTracker();

        tracker.OnConnected(); // connection A, stays anonymous
        tracker.OnConnected(); // connection B, will join a channel
        tracker.OnJoinedChannel("B", channelId: 1);

        Assert.Equal(1, tracker.GetAnonymousCount());
        Assert.Equal(1, tracker.GetViewerCount(1));
    }

    [Fact]
    public void LeavingAChannelReturnsToAnonymous()
    {
        var tracker = new PresenceTracker();
        tracker.OnConnected();
        tracker.OnJoinedChannel("A", channelId: 1);

        tracker.OnLeftChannel("A", channelId: 1);

        Assert.Equal(1, tracker.GetAnonymousCount());
        Assert.Equal(0, tracker.GetViewerCount(1));
    }

    [Fact]
    public void DisconnectingWhileJoinedRemovesFromTheChannelToo()
    {
        var tracker = new PresenceTracker();
        tracker.OnConnected();
        tracker.OnJoinedChannel("A", channelId: 1);

        tracker.OnDisconnected("A");

        Assert.Equal(0, tracker.GetAnonymousCount());
        Assert.Equal(0, tracker.GetViewerCount(1));
    }

    [Fact]
    public void MultipleViewersOfTheSameChannelAreAllCounted()
    {
        var tracker = new PresenceTracker();
        tracker.OnConnected();
        tracker.OnConnected();
        tracker.OnJoinedChannel("A", channelId: 1);
        tracker.OnJoinedChannel("B", channelId: 1);

        Assert.Equal(2, tracker.GetViewerCount(1));
        Assert.Equal(0, tracker.GetAnonymousCount());
    }

    [Fact]
    public void JoiningTheSameChannelTwiceDoesNotDoubleCountTheViewer()
    {
        var tracker = new PresenceTracker();
        tracker.OnConnected();

        tracker.OnJoinedChannel("A", channelId: 1);
        tracker.OnJoinedChannel("A", channelId: 1); // e.g. a retried JoinChannel call on the same connection

        Assert.Equal(1, tracker.GetViewerCount(1));
    }

    [Fact]
    public void JoiningADifferentChannelMovesTheViewerWithoutAnExplicitLeave()
    {
        var tracker = new PresenceTracker();
        tracker.OnConnected();

        tracker.OnJoinedChannel("A", channelId: 1);
        tracker.OnJoinedChannel("A", channelId: 2); // no OnLeftChannel(1) in between

        Assert.Equal(0, tracker.GetViewerCount(1));
        Assert.Equal(1, tracker.GetViewerCount(2));
    }

    [Fact]
    public void AConnectionThatOnlyJoinsAdminDoesNotCountAsAnonymous()
    {
        var tracker = new PresenceTracker();
        tracker.OnConnected();

        tracker.OnJoinedAdmin("A");

        Assert.Equal(0, tracker.GetAnonymousCount());
    }
    [Fact]
    public void LeavingWithAMismatchedChannelIdDoesNotCorruptAnyCount()
    {
        var tracker = new PresenceTracker();
        tracker.OnConnected();
        tracker.OnConnected();
        tracker.OnJoinedChannel("A", channelId: 1);
        tracker.OnJoinedChannel("B", channelId: 2);

        tracker.OnLeftChannel("B", channelId: 1); // B is actually on channel 2

        Assert.Equal(1, tracker.GetViewerCount(1));
        Assert.Equal(1, tracker.GetViewerCount(2));
        Assert.Equal(0, tracker.GetAnonymousCount());

        // B's real mapping survived, so a later disconnect still decrements channel 2.
        tracker.OnDisconnected("B");
        Assert.Equal(0, tracker.GetViewerCount(2));
        Assert.Equal(1, tracker.GetViewerCount(1));
    }

    [Fact]
    public void RemovingAChannelReturnsItsViewersToAnonymous()
    {
        var tracker = new PresenceTracker();
        tracker.OnConnected();
        tracker.OnConnected();
        tracker.OnConnected();
        tracker.OnJoinedChannel("A", channelId: 1);
        tracker.OnJoinedChannel("B", channelId: 1);
        tracker.OnJoinedChannel("C", channelId: 2);

        tracker.RemoveChannel(1);

        Assert.Equal(0, tracker.GetViewerCount(1));
        Assert.Equal(1, tracker.GetViewerCount(2));
        Assert.Equal(2, tracker.GetAnonymousCount());
    }

    [Fact]
    public void ViewersOfARemovedChannelCanDisconnectOrRejoinWithoutCorruptingCounts()
    {
        var tracker = new PresenceTracker();
        tracker.OnConnected();
        tracker.OnConnected();
        tracker.OnJoinedChannel("A", channelId: 1);
        tracker.OnJoinedChannel("B", channelId: 1);
        tracker.RemoveChannel(1);

        tracker.OnDisconnected("A");
        tracker.OnJoinedChannel("B", channelId: 2);

        Assert.Equal(0, tracker.GetViewerCount(1));
        Assert.Equal(1, tracker.GetViewerCount(2));
        Assert.Equal(0, tracker.GetAnonymousCount());
    }
}
