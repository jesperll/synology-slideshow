using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SynologySlideshow.Api.Services;
using SynologySlideshow.Api.Tests.TestDoubles;

namespace SynologySlideshow.Api.Tests;

public class SlideshowApiFactory : WebApplicationFactory<Program>
{
    public readonly string DbPath = Path.Combine(Path.GetTempPath(), $"slideshow-api-test-{Guid.NewGuid()}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Slideshow"] = $"Data Source={DbPath}"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<SlideShowService>();
            services.AddSingleton<SlideShowService, TestSlideShowService>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // Microsoft.Data.Sqlite pools native connections, which keeps the file
        // handle open on Windows even after the host (and its DbContext) is disposed.
        SqliteConnection.ClearAllPools();
        if (File.Exists(DbPath)) File.Delete(DbPath);
    }
}
