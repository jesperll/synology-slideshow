using Microsoft.EntityFrameworkCore;

namespace SynologySlideshow.Api.Data;

public class SlideshowDbContext : DbContext
{
    public SlideshowDbContext(DbContextOptions<SlideshowDbContext> options) : base(options)
    {
    }

    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<SlideView> SlideViews => Set<SlideView>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Channel>()
            .HasIndex(c => c.NormalizedName)
            .IsUnique();

        modelBuilder.Entity<SlideView>()
            .HasIndex(v => new { v.ChannelId, v.SlideId })
            .IsUnique();
    }
}
