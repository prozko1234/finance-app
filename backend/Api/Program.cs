using System.Text.Json.Serialization;
using FinanceApp.Api.Common;
using FinanceApp.Api.Endpoints;
using FinanceApp.Application;
using FinanceApp.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=financeapp.db";

// One password guards everything (see AuthEndpoints). Locally it may be absent — the app is
// on localhost and asking for a password every morning would be friction for nothing. Once
// deployed its absence is a mistake worth refusing to start over, because the alternative is
// a public URL serving someone's income, debts and tax profile to whoever finds it.
var appPassword = builder.Configuration["Auth:Password"];
var passwordRequired = !string.IsNullOrWhiteSpace(appPassword);

if (!builder.Environment.IsDevelopment() && !passwordRequired)
    throw new InvalidOperationException(
        "Auth__Password is not set. A deployed build refuses to run without it.");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddOpenApi();

// Unified error model (RFC 7807) + catching of unhandled exceptions.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Enums as strings in JSON (request and response): "Must", "OneOff" instead of numbers.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Allow the frontend (Vite) to call the API during local development.
const string DevCors = "dev";
builder.Services.AddCors(o => o.AddPolicy(DevCors, p => p
    .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "finance_auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
        // A fetch() gets an answer, not a redirect to a login page that does not exist here.
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });

// The auth cookie is encrypted with Data Protection keys, and by default those live inside
// the container. A redeploy would then throw them away and log the user out every single
// time — silently, looking like the password stopped working. Kept beside the database on
// the same volume, they outlive the container. Unset locally: nothing to survive there.
var keyPath = builder.Configuration["DataProtection:KeyPath"];
if (!string.IsNullOrWhiteSpace(keyPath))
    builder.Services.AddDataProtection().PersistKeysToFileSystem(Directory.CreateDirectory(keyPath));

builder.Services.AddAuthorization(o =>
{
    // Closed by default: an endpoint added later is protected unless it says otherwise, so
    // forgetting a line can never quietly publish data. Without a password (development)
    // there is nothing to check and the fallback would lock the app out of itself.
    if (passwordRequired)
        o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

var app = builder.Build();

// Behind Coolify's proxy the app sees plain HTTP; without this it would think the request
// was insecure and refuse to set a Secure cookie.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

app.UseExceptionHandler();

// Apply migrations on startup — convenient locally, the DB is created automatically.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(); // UI for manual testing: /scalar
}

app.UseCors(DevCors);

// The built SPA, when there is one (the Docker image puts it in wwwroot; a local `dotnet
// run` has no wwwroot and simply serves the API). Anonymous: the shell has to load before
// the password can be typed into it — the data behind it is what is guarded.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints(appPassword);

// Not "/": once the image ships a wwwroot, index.html answers there and a health check
// would be probing the SPA shell instead of the app.
app.MapGet("/health", () => Results.Ok(new { app = "finance-app", status = "ok" })).AllowAnonymous();
app.MapCategoryEndpoints();
app.MapTransactionEndpoints();
app.MapBudgetEndpoints();
app.MapSummaryEndpoints();
app.MapStatsEndpoints();
app.MapRecurringEndpoints();
app.MapTaxEndpoints();
app.MapSavingsEndpoints();
app.MapAllocationEndpoints();
app.MapSettingsEndpoints();

// Reset/seed helpers — local development only, never in a deployed build.
if (app.Environment.IsDevelopment()) app.MapDevEndpoints();

// Client-side routing: any unknown path is the SPA's business, not a 404 — but only when a
// wwwroot was actually shipped, so local `dotnet run` keeps returning honest 404s.
if (app.Environment.WebRootPath is not null && File.Exists(Path.Combine(app.Environment.WebRootPath, "index.html")))
{
    // Except under /api. Without this, a mistyped endpoint answers 200 with the SPA shell,
    // and a client parsing HTML as JSON fails somewhere far from the actual mistake.
    // Literal routes are more specific than this catch-all, so real endpoints still win.
    app.Map("/api/{**rest}", () => Results.NotFound()).AllowAnonymous();
    app.MapFallbackToFile("index.html").AllowAnonymous();
}

app.Run();

public partial class Program { } // marker for integration tests (WebApplicationFactory)
