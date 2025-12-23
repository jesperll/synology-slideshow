using SynologySlideshow.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add HttpClient
builder.Services.AddHttpClient();

// Add Controllers
builder.Services.AddControllers();

// Add CORS for React app (development and production)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:5174") // Vite dev server
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configuration
builder.Configuration
    .AddEnvironmentVariables()
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true, true);

// Configure Synology options
builder.Services.Configure<SynologyOptions>(
    options => builder.Configuration.GetSection("Synology").Bind(options));

// Add SlideShow service
builder.Services.AddSingleton<SlideShowService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Serve static files from wwwroot (React build)
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors(); // Enable CORS

app.UseRouting();

app.MapControllers();

// Fallback to index.html for client-side routing
app.MapFallbackToFile("index.html");

// Initialize SlideShow service
await app.Services.GetRequiredService<SlideShowService>().InitAsync();

app.Run();
