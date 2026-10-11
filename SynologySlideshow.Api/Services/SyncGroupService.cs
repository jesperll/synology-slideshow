using System.Collections.Concurrent;

namespace SynologySlideshow.Api.Services;

public class SyncGroupService
{
    private readonly ConcurrentDictionary<int, HashSet<int>> _groupsByChannel = new();

    public (bool Success, string? Error) Link(IReadOnlyCollection<int> channelIds)
    {
        var distinct = channelIds.Distinct().ToList();
        if (distinct.Count < 2)
            return (false, "A sync group needs at least two channels.");

        foreach (var id in distinct)
        {
            if (_groupsByChannel.ContainsKey(id))
                return (false, $"Channel {id} is already in a sync group.");
        }

        var members = new HashSet<int>(distinct);
        foreach (var id in distinct)
        {
            _groupsByChannel[id] = members;
        }

        return (true, null);
    }

    public void Unlink(int channelId)
    {
        if (_groupsByChannel.TryRemove(channelId, out var members))
        {
            foreach (var member in members)
            {
                if (member != channelId)
                {
                    _groupsByChannel.TryRemove(member, out _);
                }
            }
        }
    }

    public IReadOnlySet<int> GetGroupMembers(int channelId) =>
        _groupsByChannel.TryGetValue(channelId, out var members) ? members : new HashSet<int> { channelId };
}
