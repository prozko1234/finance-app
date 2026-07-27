using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FinanceApp.Api.Endpoints;

/// One password for the whole app. There are no accounts yet — the app is either yours or
/// nobody's — but a deployed build must not serve someone's finances to anyone who guesses
/// the subdomain. When accounts arrive this becomes the first user and nothing else changes.
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app, string? password)
    {
        var required = !string.IsNullOrWhiteSpace(password);

        // Anonymous by definition: these are the endpoints you call BEFORE you are let in.
        var group = app.MapGroup("/api/auth").WithTags("Auth").AllowAnonymous();

        group.MapGet("/me", (HttpContext ctx) => Results.Ok(new AuthStatus(
            required, ctx.User.Identity?.IsAuthenticated == true)));

        group.MapPost("/login", async (LoginRequest req, HttpContext ctx) =>
        {
            if (!required) return Results.NoContent(); // nothing to pass (local development)

            if (!Matches(req.Password, password!))
            {
                // A failed attempt costs a moment. Not real rate limiting, but it turns
                // "guess a few million" into "guess a few thousand a day" for free.
                await Task.Delay(400);
                return Results.Problem(title: "Невірний пароль", statusCode: StatusCodes.Status401Unauthorized);
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "owner")], CookieAuthenticationDefaults.AuthenticationScheme);

            await ctx.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                // Persistent, or the PWA would ask for the password every time it is reopened.
                new AuthenticationProperties { IsPersistent = true });

            return Results.NoContent();
        });

        group.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        return app;
    }

    /// Compared without an early exit, so the password cannot be recovered one character at
    /// a time by timing the answer.
    private static bool Matches(string? given, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(given ?? string.Empty), Encoding.UTF8.GetBytes(expected));
}

/// These two live here rather than in Application/Contracts on purpose: there is no use case
/// behind them: signing in is entirely an Api-layer concern (a cookie), and the Application
/// layer has no business knowing the app has a door.
public record LoginRequest(string Password);

/// <paramref name="Required"/> is false in local development, where no password is set —
/// then the UI must not show a login screen that cannot be passed.
public record AuthStatus(bool Required, bool Authenticated);
