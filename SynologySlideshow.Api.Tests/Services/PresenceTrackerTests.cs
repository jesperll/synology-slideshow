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
}
