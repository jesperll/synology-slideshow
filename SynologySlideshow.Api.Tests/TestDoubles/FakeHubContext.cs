using Microsoft.AspNetCore.SignalR;
using SynologySlideshow.Api.Realtime;

namespace SynologySlideshow.Api.Tests.TestDoubles;

public class FakeHubContext : IHubContext<SlideshowHub>
{
    public List<(string Group, string Method, object?[] Args)> Sent { get; } = new();

    public IHubClients Clients { get; }
    public IGroupManager Groups => throw new NotSupportedException("Not used by ChannelPlaybackService.");

    public FakeHubContext()
    {
        Clients = new FakeHubClients(this);
    }

    private class FakeHubClients : IHubClients
    {
        private readonly FakeHubContext _owner;
        public FakeHubClients(FakeHubContext owner) => _owner = owner;

        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => new FakeClientProxy(_owner, groupName);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private class FakeClientProxy : IClientProxy
    {
        private readonly FakeHubContext _owner;
        private readonly string _group;
        public FakeClientProxy(FakeHubContext owner, string group)
        {
            _owner = owner;
            _group = group;
        }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            _owner.Sent.Add((_group, method, args));
            return Task.CompletedTask;
        }
    }
}
