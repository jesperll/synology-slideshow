using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SynologySlideshow.Api.Data;
using Xunit;

namespace SynologySlideshow.Api.Tests.Data;

public class SlideshowDbContextTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"slideshow-test-{Guid.NewGuid()}.db");
    private readonly SlideshowDbContext _context;

    public SlideshowDbContextTests()
    {
        var options = new DbContextOptionsBuilder<SlideshowDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _context = new SlideshowDbContext(options);
        _context.Database.Migrate();
    }

    [Fact]
    public async Task SavesAndRetrievesChannel()
    {
        _context.Channels.Add(new Channel { Name = "kitchen", NormalizedName = "KITCHEN" });
        await _context.SaveChangesAsync();

        var saved = await _context.Channels.SingleAsync(c => c.Name == "kitchen");

        Assert.Equal("kitchen", saved.Name);
        Assert.True(saved.IsPaused);
        Assert.Null(saved.CurrentAlbumId);
    }

    [Fact]
    public async Task RejectsDuplicateNormalizedName()
    {
        _context.Channels.Add(new Channel { Name = "kitchen", NormalizedName = "KITCHEN" });
        await _context.SaveChangesAsync();

        _context.Channels.Add(new Channel { Name = "Kitchen ", NormalizedName = "KITCHEN" });

        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    public void Dispose()
    {
        _context.Dispose();
        // Microsoft.Data.Sqlite pools native connections, which keeps the file
        // handle open on Windows even after the DbContext is disposed.
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
