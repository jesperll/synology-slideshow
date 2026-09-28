using System.Collections.Concurrent;

namespace SynologySlideshow.Api.Services;

public class PresenceTracker
{
    private int _totalConnections;
    private readonly ConcurrentDictionary<string, int> _connectionChannel = new();
    private readonly ConcurrentDictionary<int, int> _viewerCounts = new();

    public void OnConnected() => Interlocked.Increment(ref _totalConnections);

    public void OnDisconnected(string connectionId)
    {
        Interlocked.Decrement(ref _totalConnections);
        if (_connectionChannel.TryRemove(connectionId, out var channelId))
        {
            _viewerCounts.AddOrUpdate(channelId, 0, (_, count) => Math.Max(0, count - 1));
        }
    }

    public void OnJoinedChannel(string connectionId, int channelId)
    {
        _connectionChannel[connectionId] = channelId;
        _viewerCounts.AddOrUpdate(channelId, 1, (_, count) => count + 1);
    }

    public void OnLeftChannel(string connectionId, int channelId)
    {
        if (_connectionChannel.TryRemove(connectionId, out _))
        {
            _viewerCounts.AddOrUpdate(channelId, 0, (_, count) => Math.Max(0, count - 1));
        }
    }

    public int GetViewerCount(int channelId) => _viewerCounts.TryGetValue(channelId, out var count) ? count : 0;

    public int GetAnonymousCount() => Math.Max(0, _totalConnections - _viewerCounts.Values.Sum());
}
