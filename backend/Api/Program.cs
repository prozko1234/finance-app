using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FinanceApp.Api.Common;
using FinanceApp.Api.Endpoints;
using FinanceApp.Application;
using FinanceApp.Application.Auth;
using Microsoft.AspNetCore.Authentication;
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

// An account guards everything (see AuthEndpoints). The password now lives hashed in the
// database; configuration only BOOTSTRAPS the owner on first start, and is ignored once the
// account exists — otherwise a redeploy would silently undo a password change.
//
// Locally both may be absent: the app is on localhost and asking for a password every
// morning would be friction for nothing. Deployed, the door is always there.
var bootstrapPassword = builder.Configuration["Auth:Password"];
var bootstrapEmail = builder.Configuration["Auth:Email"];
var hasBootstrap = !string.IsNullOrWhiteSpace(bootstrapPassword);

// Deployed builds are locked whatever the configuration says. Development is open unless a
// password was configured, so accounts can still be exercised locally by setting one.
var authRequired = !builder.Environment.IsDevelopment() || hasBootstrap;

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

// Once the domain has a certificate this should be `true`: a cookie without Secure is sent
// over plain HTTP too, so anyone on the same network can lift the session. It is not the
// default yet only because setting it before TLS is on would lock the app out of itself —
// the browser would refuse to store the cookie at all.
var secureCookies = builder.Configuration.GetValue("Auth:SecureCookies", false);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "finance_auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        // A month, sliding: the app is opened most days, so in practice the password is
        // asked for about once — which is what was wanted.
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
        // A fetch() gets an answer, not a redirect to a login page that does not exist here.
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };

        // What makes "sign out everywhere" and a password change actually end other sessions.
        // A cookie is self-contained — without this check it stays valid for its full month
        // no matter what happened to the account, on a device that may no longer be yours.
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var id = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var stamp = ctx.Principal?.FindFirstValue(AuthEndpoints.StampClaim);

            if (!int.TryParse(id, out var userId) || string.IsNullOrEmpty(stamp))
            {
                // A cookie from before accounts existed: it has no user behind it.
                ctx.RejectPrincipal();
                return;
            }

            var accounts = ctx.HttpContext.RequestServices.GetRequiredService<IAccountService>();
            if (await accounts.FindValidAsync(userId, stamp, ctx.HttpContext.RequestAborted) is null)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

// The login endpoint is the one place worth guessing at, and a password is only as good as
// the number of tries it survives. Per IP so one jammed attacker cannot lock out the owner.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.LoginRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            // Ten tries per five minutes is invisible to someone typing a password they know
            // and hopeless for someone guessing one they do not.
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5),
        }));
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
    if (authRequired)
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

    if (authRequired)
    {
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();

        if (hasBootstrap)
        {
            // Only takes effect on an empty database. The very first run of this build turns
            // the configured password into the owner's account; later runs do nothing, which
            // is what lets the password be changed from inside the app for good.
            var email = string.IsNullOrWhiteSpace(bootstrapEmail)
                // A placeholder rather than a hard failure: a redeploy that refuses to start
                // over a missing variable is a worse outcome than a login name to change
                // later (POST /api/auth/email).
                ? "owner@finance.local"
                : bootstrapEmail;

            if (await accounts.EnsureOwnerAsync(email, bootstrapPassword!))
                app.Logger.LogInformation("Owner account created for {Email} from configuration.", email);
        }
        else if (!await accounts.HasOwnerAsync())
        {
            // Deployed, locked, and nobody holds the key: every request would 401 forever.
            // Refusing to start is the honest version of that, and says how to fix it.
            throw new InvalidOperationException(
                "No account exists and Auth__Password is not set. A deployed build refuses to " +
                "run without a way in — set Auth__Password (and optionally Auth__Email) once.");
        }
    }
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

app.UseRateLimiter();

app.MapAuthEndpoints(authRequired);

// Not "/": once the image ships a wwwroot, index.html answers there and a health check
// would be probing the SPA shell instead of the app.
app.MapGet("/health", () => Results.Ok(new { app = "finance-app", status = "ok" })).AllowAnonymous();
app.MapCategoryEndpoints();
app.MapTransactionEndpoints();
app.MapOpeningBalanceEndpoints();
app.MapSummaryEndpoints();
app.MapStatsEndpoints();
app.MapRecurringEndpoints();
app.MapTaxEndpoints();
app.MapSavingsEndpoints();
app.MapEnvelopeEndpoints();
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
