// Load .env (if present) into environment variables so settings such as
// Admin:Username / Admin:Password resolve from it. Env vars take precedence
// over appsettings.json. Must run before the builder reads configuration.
LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// cr7stream: register logic layer with DI. Controllers depend only on the
// clean interfaces; the messy data/scraping work lives in cr7stream.Logic.
builder.Services.AddHttpClient<cr7stream.Logic.Scrapers.ITotalSportekScraper, cr7stream.Logic.Scrapers.TotalSportekScraper>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<cr7stream.Logic.Services.ILogoService, cr7stream.Services.LogoService>();
builder.Services.AddScoped<cr7stream.Logic.Services.IFixtureProvider, cr7stream.Logic.Services.JsonFixtureProvider>();
builder.Services.AddScoped<cr7stream.Logic.Services.IScraperSettingsProvider, cr7stream.Logic.Services.ScraperSettingsProvider>();
builder.Services.AddScoped<cr7stream.Logic.IScrapperLogic, cr7stream.Logic.ScrapperLogic>();
builder.Services.AddScoped<cr7stream.Logic.IHomeControllerLogic, cr7stream.Logic.HomeControllerLogic>();
builder.Services.AddScoped<cr7stream.Logic.IMatchesControllerLogic, cr7stream.Logic.MatchesControllerLogic>();
builder.Services.AddScoped<cr7stream.Logic.IAdminLogic, cr7stream.Logic.AdminLogic>();
builder.Services.AddHostedService<cr7stream.Services.AutoScraperService>();

// Admin panel uses its own cookie authentication scheme.
builder.Services.AddAuthentication("Admin")
    .AddCookie("Admin", options =>
    {
        options.LoginPath = "/admin/login";
        options.LogoutPath = "/admin/logout";
        options.AccessDeniedPath = "/admin/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Cookie.Name = "CR7StreamAdmin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

var app = builder.Build();

// Reads KEY=VALUE pairs from a .env file (project root when running via
// `dotnet run`) and promotes them to environment variables. Known ADMIN_*
// keys are mapped to the "Admin:*" configuration section (ASP.NET maps the
// "__" separator in env var names to the ":" config separator).
static void LoadDotEnv()
{
    var path = Path.Combine(Directory.GetCurrentDirectory(), ".env");
    if (!File.Exists(path))
    {
        path = Path.Combine(AppContext.BaseDirectory, ".env");
    }

    if (!File.Exists(path))
    {
        return;
    }

    foreach (var raw in File.ReadAllLines(path))
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            continue;
        }

        var idx = line.IndexOf('=');
        if (idx < 0)
        {
            continue;
        }

        var key = line[..idx].Trim();
        var value = line[(idx + 1)..].Trim().Trim('"');

        var envKey = key switch
        {
            "ADMIN_USERNAME" => "Admin__Username",
            "ADMIN_PASSWORD" => "Admin__Password",
            _ => key
        };

        Environment.SetEnvironmentVariable(envKey, value);
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

// Cache logos for 30 days.
app.Map("/img/logos/{**path}", async (HttpContext context) =>
{
    var logosDir = Path.GetFullPath(Path.Combine(app.Environment.WebRootPath, "img", "logos"));
    var filePath = Path.GetFullPath(Path.Combine(logosDir, context.Request.RouteValues["path"]?.ToString() ?? ""));
    if (!filePath.StartsWith(logosDir, StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 400;
        return;
    }
    if (File.Exists(filePath))
    {
        context.Response.Headers.CacheControl = "public, max-age=2592000";
        context.Response.Headers.Expires = DateTimeOffset.UtcNow.AddDays(30).ToString("R");
        await context.Response.SendFileAsync(filePath);
    }
    else
    {
        context.Response.StatusCode = 404;
    }
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

