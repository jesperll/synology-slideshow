using System.Collections.Concurrent;

namespace SynologySlideshow.Api.Services;

public class PresenceTracker
{
    private int _totalConnections;
    private readonly ConcurrentDictionary<string, int> _connectionChannel = new();
    private readonly ConcurrentDictionary<int, int> _viewerCounts = new();
    private readonly ConcurrentDictionary<string, byte> _adminConnections = new();

    public void OnConnected() => Interlocked.Increment(ref _totalConnections);

    public void OnDisconnected(string connectionId)
    {
        Interlocked.Decrement(ref _totalConnections);
        if (_connectionChannel.TryRemove(connectionId, out var channelId))
        {
            _viewerCounts.AddOrUpdate(channelId, 0, (_, count) => Math.Max(0, count - 1));
        }
        _adminConnections.TryRemove(connectionId, out _);
    }

    public void OnJoinedChannel(string connectionId, int channelId)
    {
        if (_connectionChannel.TryGetValue(connectionId, out var existingChannelId))
        {
            if (existingChannelId == channelId) return; // already counted for this channel; no-op
            _viewerCounts.AddOrUpdate(existingChannelId, 0, (_, count) => Math.Max(0, count - 1));
        }
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

    public void OnJoinedAdmin(string connectionId) => _adminConnections[connectionId] = 0;

    public int GetViewerCount(int channelId) => _viewerCounts.TryGetValue(channelId, out var count) ? count : 0;

    public int GetAnonymousCount() => Math.Max(0, _totalConnections - _viewerCounts.Values.Sum() - _adminConnections.Count);
}
