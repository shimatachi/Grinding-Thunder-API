using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Add Razor Pages
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/AccessDenied");
    options.Conventions.AllowAnonymousToPage("/Logout");
});

// Configure Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "GrindingThunder.Admin.Session";
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.AccessDeniedPath = "/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

// Configure HttpContextAccessor and Bearer Token Handler
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddTransient<BearerTokenHandler>();

// Configure typed ApiClient
var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5034";
builder.Services.AddHttpClient<IApiClient, ApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/");
})
.AddHttpMessageHandler<BearerTokenHandler>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();

public partial class Program { }

