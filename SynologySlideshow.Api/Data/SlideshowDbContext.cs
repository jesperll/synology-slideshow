using Microsoft.EntityFrameworkCore;

namespace SynologySlideshow.Api.Data;

public class SlideshowDbContext : DbContext
{
    public SlideshowDbContext(DbContextOptions<SlideshowDbContext> options) : base(options)
    {
    }

    public DbSet<Channel> Channels => Set<Channel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Channel>()
            .HasIndex(c => c.NormalizedName)
            .IsUnique();
    }
}
