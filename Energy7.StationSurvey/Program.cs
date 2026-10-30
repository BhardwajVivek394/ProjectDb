using E7MRIWeb.Service;
using Energy7.StationSurvey.Interface;
using Energy7.StationSurvey.Service;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddHttpContextAccessor();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddScoped<ISiteService, SiteService>();
builder.Services.AddScoped<IZoneService, ZoneService>();
builder.Services.AddScoped<IDivisionService, DivisionService>();
builder.Services.AddScoped<IAssetTypeService, AssetTypeService>();


var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.UseSession();        // ? must be before MapControllerRoute

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Login}/{action=Index}/{id?}");

app.Run();