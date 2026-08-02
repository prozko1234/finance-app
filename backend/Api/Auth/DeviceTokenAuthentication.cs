using System.Security.Claims;
using System.Text.Encodings.Web;
using FinanceApp.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FinanceApp.Api.Auth;

/// `Authorization: Bearer <token>` as a second way in, alongside the cookie.
///
/// It exists because the cookie cannot reach two places that need the API: the native iOS
/// shell, which serves the app from its own origin, and the widget extension, which is a
/// different process entirely. The web app is untouched and keeps using the cookie.
public sealed class DeviceTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IDeviceTokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "DeviceToken";

    private const string Prefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            // NoResult, not Fail: no bearer token simply means this request is not for this
            // scheme, and the cookie scheme still gets its turn.
            return AuthenticateResult.NoResult();
        }

        var secret = header[Prefix.Length..].Trim();
        var account = await tokens.AuthenticateAsync(secret, Context.RequestAborted);
        if (account is null) return AuthenticateResult.Fail("Невідомий або відкликаний токен");

        // The same claims a cookie session carries, so every endpoint reads the user the
        // same way regardless of how the caller got in.
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
                new Claim(ClaimTypes.Name, account.Email),
            ],
            Scheme);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme));
    }
}
