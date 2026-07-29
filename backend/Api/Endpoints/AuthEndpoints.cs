using System.Security.Claims;
using FinanceApp.Api.Common;
using FinanceApp.Application.Auth;
using FinanceApp.Domain.Auth;
using FinanceApp.Domain.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;

namespace FinanceApp.Api.Endpoints;

/// The door. Signing in is an Api-layer concern (a cookie); who the user is and whether the
/// password fits lives in <see cref="IAccountService"/>.
///
/// One account, by design — see <see cref="User"/>. There is no registration endpoint: the
/// owner is created from configuration on first start, and after that the only way in is the
/// password.
public static class AuthEndpoints
{
    /// Identifies the session's user. Read back on every request to check the stamp.
    public const string StampClaim = "stamp";

    /// Named so Program.cs can put a limiter on the one endpoint worth brute-forcing.
    public const string LoginRateLimit = "login";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app, bool required)
    {
        // Anonymous by definition: these are the endpoints called BEFORE being let in.
        var group = app.MapGroup("/api/auth").WithTags("Auth").AllowAnonymous();

        group.MapGet("/me", (HttpContext ctx) => Results.Ok(new AuthStatus(
            required,
            ctx.User.Identity?.IsAuthenticated == true,
            ctx.User.FindFirstValue(ClaimTypes.Name))));

        group.MapPost("/login", async (LoginRequest req, HttpContext ctx, IAccountService accounts, CancellationToken ct) =>
        {
            if (!required) return Results.NoContent(); // no door at all (local development)

            var result = await accounts.AuthenticateAsync(req.Email, req.Password, ct);
            if (!result.IsSuccess)
            {
                // 401, not the 400 a validation error would map to: this is "not let in",
                // and the frontend tells the two apart to decide where to send the user.
                return Results.Problem(
                    title: result.Error.Message, statusCode: StatusCodes.Status401Unauthorized);
            }

            await SignInAsync(ctx, result.Value!);
            return Results.NoContent();
        }).RequireRateLimiting(LoginRateLimit);

        group.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        // The rest need an open session: they change the account they are signed into.
        var account = app.MapGroup("/api/auth").WithTags("Auth").RequireAuthorization();

        account.MapPost("/password", async (
            ChangePasswordRequest req, HttpContext ctx, IAccountService accounts, CancellationToken ct) =>
        {
            if (CurrentUserId(ctx) is not { } userId) return Results.Unauthorized();

            var result = await accounts.ChangePasswordAsync(userId, req.CurrentPassword, req.NewPassword, ct);
            if (!result.IsSuccess) return result.Error.ToProblem();

            // Changing the password ended every session, this one included. Signing back in
            // with the new stamp keeps the user where they were instead of throwing them out
            // of the app they just secured.
            await SignInAsync(ctx, result.Value!);
            return Results.NoContent();
        });

        account.MapPost("/email", async (
            ChangeEmailRequest req, HttpContext ctx, IAccountService accounts, CancellationToken ct) =>
        {
            if (CurrentUserId(ctx) is not { } userId) return Results.Unauthorized();

            var result = await accounts.ChangeEmailAsync(userId, req.Password, req.Email, ct);
            if (!result.IsSuccess) return result.Error.ToProblem();

            // The address is part of the cookie's claims, so the session has to be reissued
            // or /me would keep reporting the old one until the next login.
            await SignInAsync(ctx, result.Value!);
            return Results.NoContent();
        });

        account.MapPost("/sign-out-everywhere", async (
            HttpContext ctx, IAccountService accounts, CancellationToken ct) =>
        {
            if (CurrentUserId(ctx) is not { } userId) return Results.Unauthorized();

            var result = await accounts.SignOutEverywhereAsync(userId, ct);
            if (!result.IsSuccess) return result.Error.ToProblem();

            // Including this device: "everywhere" that spared the phone in your hand would
            // be a lie, and the password is right there.
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        return app;
    }

    private static async Task SignInAsync(HttpContext ctx, Account account)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
                new Claim(ClaimTypes.Name, account.Email),
                new Claim(StampClaim, account.SecurityStamp),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        await ctx.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            // Persistent, or the PWA would ask for the password every time it is reopened.
            new AuthenticationProperties { IsPersistent = true });
    }

    private static int? CurrentUserId(HttpContext ctx) =>
        int.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

/// These live here rather than in Application/Contracts on purpose: signing in is entirely an
/// Api-layer concern (a cookie), and the Application layer has no business knowing the app
/// has a door.
public record LoginRequest(string Email, string Password);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record ChangeEmailRequest(string Password, string Email);

/// <paramref name="Required"/> is false in local development, where no account is set up —
/// then the UI must not show a login screen that cannot be passed.
/// <paramref name="Email"/> is null until signed in; the UI shows it in settings.
public record AuthStatus(bool Required, bool Authenticated, string? Email);
