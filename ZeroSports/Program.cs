var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// ZeroSports: register logic layer with DI. Controllers depend only on the
// clean interfaces; the messy data/scraping work lives in ZeroSports.Logic.
builder.Services.AddScoped<ZeroSports.Logic.Services.IFixtureProvider, ZeroSports.Logic.Services.JsonFixtureProvider>();
builder.Services.AddScoped<ZeroSports.Logic.IScrapperLogic, ZeroSports.Logic.ScrapperLogic>();
builder.Services.AddScoped<ZeroSports.Logic.IHomeControllerLogic, ZeroSports.Logic.HomeControllerLogic>();
builder.Services.AddScoped<ZeroSports.Logic.IMatchesControllerLogic, ZeroSports.Logic.MatchesControllerLogic>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
