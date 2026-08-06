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
/// Several accounts are possible — see <see cref="User"/>. The owner is created from
/// configuration on first start; everybody else arrives through an <see cref="Invite"/> the
/// owner handed out. There is no open registration: this is one person's server.
public static class AuthEndpoints
{
    /// Identifies the session's user. Read back on every request to check the stamp.
    public const string StampClaim = "stamp";

    /// Named so Program.cs can put a limiter on the endpoints worth brute-forcing.
    public const string LoginRateLimit = "login";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app, bool required)
    {
        // Anonymous by definition: these are the endpoints called BEFORE being let in.
        var group = app.MapGroup("/api/auth").WithTags("Auth").AllowAnonymous();

        group.MapGet("/me", async (HttpContext ctx, IAccountService accounts, CancellationToken ct) =>
        {
            var id = CurrentUserId(ctx);
            // Read from the database rather than carried in the cookie: ownership is not
            // something a session should be able to assert after the fact.
            var isOwner = id is not null && await accounts.IsOwnerAsync(id.Value, ct);

            return Results.Ok(new AuthStatus(
                required,
                ctx.User.Identity?.IsAuthenticated == true,
                ctx.User.FindFirstValue(ClaimTypes.Name),
                isOwner));
        });

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

        // Anonymous because the person registering has no session yet — the invite code IS
        // the proof. Rate-limited alongside login: a register endpoint that answers quickly
        // is a way to test codes, and it creates rows besides.
        group.MapPost("/register", async (
            RegisterRequest req, HttpContext ctx, IInviteService invites, CancellationToken ct) =>
        {
            var result = await invites.RedeemAsync(req.Code, req.Email, req.Password, ct);
            if (!result.IsSuccess) return result.Error.ToProblem();

            await SignInAsync(ctx, result.Value!);
            return Results.NoContent();
        }).RequireRateLimiting(LoginRateLimit);

        group.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        // Anonymous on purpose: this is how a freshly installed app gets in, when there is
        // no session yet to prove anything with. Rate-limited like the login it really is.
        group.MapPost("/device-tokens", async (
            IssueDeviceTokenRequest req, IDeviceTokenService tokens, CancellationToken ct) =>
        {
            if (!required)
            {
                // Development has no door, so there is no credential to hand out — and
                // pretending otherwise would give the phone a token that means nothing.
                return Results.Problem(
                    title: "Застосунок без пароля не видає токенів пристроїв",
                    statusCode: StatusCodes.Status409Conflict);
            }

            // Shape first, so that anything the service rejects afterwards can only be the
            // credentials — and gets the 401 that a failed sign-in deserves, rather than the
            // 400 that a validation error would map to. The client tells the two apart.
            var name = req.Name?.Trim() ?? "";
            if (name.Length == 0 || name.Length > DeviceToken.MaxNameLength)
            {
                return Error.Validation(
                    $"Назва пристрою обов'язкова і не довша за {DeviceToken.MaxNameLength} символів").ToProblem();
            }

            var result = await tokens.IssueAsync(req.Email, req.Password, name, ct);
            if (!result.IsSuccess)
            {
                return Results.Problem(
                    title: result.Error.Message, statusCode: StatusCodes.Status401Unauthorized);
            }

            return Results.Ok(result.Value);
        }).RequireRateLimiting(LoginRateLimit);

        // The rest need an open session: they change the account they are signed into.
        var account = app.MapGroup("/api/auth").WithTags("Auth").RequireAuthorization();

        account.MapGet("/device-tokens", async (
            HttpContext ctx, IDeviceTokenService tokens, CancellationToken ct) =>
        {
            if (CurrentUserId(ctx) is not { } userId) return Results.Unauthorized();

            return Results.Ok(await tokens.ListAsync(userId, ct));
        });

        account.MapDelete("/device-tokens/{id:int}", async (
            int id, HttpContext ctx, IDeviceTokenService tokens, CancellationToken ct) =>
        {
            if (CurrentUserId(ctx) is not { } userId) return Results.Unauthorized();

            var result = await tokens.RevokeAsync(userId, id, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
        });

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

        // The owner's side of invites. Everything here is refused for anyone else, in the
        // service rather than here, so the rule holds however it is called.
        account.MapPost("/invites", async (
            CreateInviteRequest req, HttpContext ctx, IInviteService invites, CancellationToken ct) =>
        {
            var id = CurrentUserId(ctx);
            if (id is null) return Results.Unauthorized();

            var result = await invites.CreateAsync(id.Value, req.Note, ct);
            // The code travels in this one response and is never readable again — the row
            // holds only its hash.
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        });

        account.MapGet("/invites", async (
            HttpContext ctx, IInviteService invites, CancellationToken ct) =>
        {
            var id = CurrentUserId(ctx);
            if (id is null) return Results.Unauthorized();

            var result = await invites.ListAsync(id.Value, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        });

        account.MapDelete("/invites/{id:int}", async (
            int id, HttpContext ctx, IInviteService invites, CancellationToken ct) =>
        {
            var me = CurrentUserId(ctx);
            if (me is null) return Results.Unauthorized();

            var result = await invites.RevokeAsync(me.Value, id, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
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

/// <param name="Code">The invite code from the link. The whole authorization to exist here.</param>
public record RegisterRequest(string Code, string Email, string Password);

public record CreateInviteRequest(string Note);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record ChangeEmailRequest(string Password, string Email);

/// <paramref name="Name"/> is what the device will be called in the list — "iPhone", not a
/// serial number. The client picks it; the owner can read it later and decide what to cut off.
public record IssueDeviceTokenRequest(string Email, string Password, string Name);

/// <paramref name="Required"/> is false in local development, where no account is set up —
/// then the UI must not show a login screen that cannot be passed.
/// <paramref name="Email"/> is null until signed in; the UI shows it in settings.
/// <param name="IsOwner">Whether this account may hand out invites. The frontend shows the
/// invite section on it, and the server refuses regardless of what the frontend decided.</param>
public record AuthStatus(bool Required, bool Authenticated, string? Email, bool IsOwner = false);
