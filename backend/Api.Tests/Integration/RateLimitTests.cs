using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FinanceApp.Api.Tests.Integration;

/// The login limiter, and the thing it silently was not doing.
///
/// It partitions by remote address so that one person guessing passwords cannot shut the door
/// on everybody else. Behind Coolify the app never saw a remote address: forwarded headers are
/// trusted from loopback by default and the proxy is a different container, so every request
/// arrived wearing the same one. Ten wrong passwords from anyone locked out the whole
/// instance — which is exactly the failure the partition exists to prevent, and it was
/// invisible while there was one account.
public class RateLimitTests
{
    private const string OwnerPassword = "correct horse battery";

    /// Ten per five minutes, from Program.cs. The eleventh is the one that matters.
    private const int Permitted = 10;

    private sealed class LockedApi : TestApiFactory
    {
        protected override string? Password => OwnerPassword;
    }

    private static HttpClient From(LockedApi api, string ip)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        return client;
    }

    private static Task<HttpResponseMessage> WrongPassword(HttpClient client) =>
        client.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestApiFactory.OwnerEmail,
            password = "not the password",
        });

    /// The actual fix, asserted directly — because the behaviour test below cannot see it.
    /// In a test server the remote address is loopback, which the defaults already trust, so
    /// forwarded headers are honoured there whether or not this is configured. Behind Coolify
    /// the caller is another container, the defaults reject it, and the partition collapses to
    /// one bucket for everybody. Only the configuration tells the two apart.
    [Fact]
    public void Forwarded_headers_are_trusted_from_the_proxy()
    {
        using var api = new LockedApi();
        api.CreateClient(); // builds the host

        var options = api.Services
            .GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Empty(options.KnownNetworks);
        Assert.Empty(options.KnownProxies);
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));
    }

    [Fact]
    public async Task Guessing_from_one_address_locks_out_only_that_address()
    {
        using var api = new LockedApi();
        var attacker = From(api, "203.0.113.7");

        for (var i = 0; i < Permitted; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await WrongPassword(attacker)).StatusCode);

        // Spent: the next try never reaches the password check.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await WrongPassword(attacker)).StatusCode);

        // And somebody else is still let in — the point of partitioning at all. Before the
        // forwarded headers were trusted, this was a 429 too.
        var owner = From(api, "198.51.100.4");
        var res = await owner.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestApiFactory.OwnerEmail,
            password = OwnerPassword,
        });

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    /// Registering is a door too: it creates rows and it tests invite codes, so it shares the
    /// limiter rather than standing beside it as an unguarded way in.
    [Fact]
    public async Task Registering_is_limited_the_same_way()
    {
        using var api = new LockedApi();
        var guesser = From(api, "203.0.113.9");

        for (var i = 0; i < Permitted; i++)
        {
            var res = await guesser.PostAsJsonAsync("/api/auth/register", new
            {
                code = "made-up", email = $"a{i}@x.com", password = "long enough one",
            });
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        var blocked = await guesser.PostAsJsonAsync("/api/auth/register", new
        {
            code = "made-up", email = "z@x.com", password = "long enough one",
        });

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }
}
