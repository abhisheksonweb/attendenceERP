using System.Security.Claims;
using MedicalCollege.Application.Extensions;
using MedicalCollege.Domain.Enums;
using MedicalCollege.Infrastructure.Extensions;
using MedicalCollege.Infrastructure.Seed;
using MedicalCollege.Web.Filters;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Always use Frontend/App_Data (project root), never bin/.../App_Data, so student
// logins survive rebuilds and match the JSON files you edit under the project.
var appDataPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
Directory.CreateDirectory(appDataPath);
Directory.CreateDirectory(Path.Combine(builder.Environment.WebRootPath, "uploads", "profiles"));
Console.WriteLine($"[App] Data store: {appDataPath}");

builder.Services.AddInfrastructure(appDataPath, builder.Environment.WebRootPath);
builder.Services.AddApplicationServices();

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<RequirePasswordChangeFilter>();
});
builder.Services.AddScoped<RequireStudentProfileFilter>();
builder.Services.AddScoped<RequirePasswordChangeFilter>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Cookie.Name = "MedCollege.Auth";
        options.Cookie.HttpOnly = true;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", p => p.RequireRole(nameof(UserRole.Admin)));
    options.AddPolicy("Student", p => p.RequireRole(nameof(UserRole.Student)));
});

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = "MedCollege.Session";
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
