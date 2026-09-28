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
        // Only leave if the connection is actually tracked on this channel: a mismatched
        // channelId must neither drop the connection's real mapping nor decrement an
        // unrelated channel's count. The KeyValuePair overload removes atomically only
        // when the stored value still matches.
        if (_connectionChannel.TryRemove(new KeyValuePair<string, int>(connectionId, channelId)))
        {
            _viewerCounts.AddOrUpdate(channelId, 0, (_, count) => Math.Max(0, count - 1));
        }
    }

    // Forgets a deleted channel: its viewer count goes away and its viewers' connections
    // are no longer mapped to it, so they count as anonymous again instead of being
    // attributed to a channel the admin snapshot will never list.
    public void RemoveChannel(int channelId)
    {
        _viewerCounts.TryRemove(channelId, out _);
        foreach (var entry in _connectionChannel)
        {
            if (entry.Value == channelId)
            {
                // Value-matched removal so a connection that concurrently moved to another
                // channel keeps its new mapping.
                _connectionChannel.TryRemove(entry);
            }
        }
    }

    public void OnJoinedAdmin(string connectionId) => _adminConnections[connectionId] = 0;

    public int GetViewerCount(int channelId) => _viewerCounts.TryGetValue(channelId, out var count) ? count : 0;

    public int GetAnonymousCount() => Math.Max(0, _totalConnections - _viewerCounts.Values.Sum() - _adminConnections.Count);
}
