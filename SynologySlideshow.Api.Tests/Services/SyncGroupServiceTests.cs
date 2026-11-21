using SynologySlideshow.Api.Services;
using Xunit;

namespace SynologySlideshow.Api.Tests.Services;

public class SyncGroupServiceTests
{
    [Fact]
    public void LinkingTwoChannelsGroupsThemTogether()
    {
        var service = new SyncGroupService();

        var (success, error) = service.Link(new[] { 1, 2 });

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(new HashSet<int> { 1, 2 }, service.GetGroupMembers(1));
        Assert.Equal(new HashSet<int> { 1, 2 }, service.GetGroupMembers(2));
    }

    [Fact]
    public void LinkingFewerThanTwoChannelsFails()
    {
        var service = new SyncGroupService();

        var (success, error) = service.Link(new[] { 1 });

        Assert.False(success);
        Assert.NotNull(error);
    }

    [Fact]
    public void LinkingAChannelAlreadyInAGroupFailsAndLeavesTheOriginalGroupIntact()
    {
        var service = new SyncGroupService();
        service.Link(new[] { 1, 2 });

        var (success, error) = service.Link(new[] { 2, 3 });

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Equal(new HashSet<int> { 1, 2 }, service.GetGroupMembers(1));
    }

    [Fact]
    public void AChannelNotInAnyGroupReturnsItselfAsItsOnlyMember()
    {
        var service = new SyncGroupService();

        Assert.Equal(new HashSet<int> { 42 }, service.GetGroupMembers(42));
    }

    [Fact]
    public void UnlinkingAnyMemberDissolvesTheWholeGroup()
    {
        var service = new SyncGroupService();
        service.Link(new[] { 1, 2, 3 });

        service.Unlink(2);

        Assert.Equal(new HashSet<int> { 1 }, service.GetGroupMembers(1));
        Assert.Equal(new HashSet<int> { 2 }, service.GetGroupMembers(2));
        Assert.Equal(new HashSet<int> { 3 }, service.GetGroupMembers(3));
    }

    [Fact]
    public void GroupsOfMoreThanTwoAreSupported()
    {
        var service = new SyncGroupService();

        var (success, _) = service.Link(new[] { 1, 2, 3 });

        Assert.True(success);
        Assert.Equal(new HashSet<int> { 1, 2, 3 }, service.GetGroupMembers(3));
    }
}
