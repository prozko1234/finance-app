using System.Net;
using System.Net.Http.Json;

namespace FinanceApp.Api.Tests.Integration;

/// The lock itself. These tests are the reason the app can be given a public URL at all,
/// so they check the door rather than the handle: every data endpoint, not just one.
public class AuthTests
{
    private const string OwnerPassword = "correct horse battery";

    private sealed class LockedApi : TestApiFactory
    {
        protected override string? Password => OwnerPassword;
    }

    /// Redirects are not followed: an unauthenticated API call must ANSWER 401, and a
    /// client following a redirect would see a 200 and think it was let in.
    private static HttpClient Client(LockedApi api) =>
        api.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    private static Task<HttpResponseMessage> LogIn(HttpClient client, string? password = null) =>
        client.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestApiFactory.OwnerEmail,
            password = password ?? OwnerPassword,
        });

    [Theory]
    [InlineData("/api/summary/safe-to-spend")]
    [InlineData("/api/transactions")]
    [InlineData("/api/stats")]
    [InlineData("/api/categories")]
    [InlineData("/api/budget")]
    [InlineData("/api/savings")]
    [InlineData("/api/allocations")]
    [InlineData("/api/settings")]
    [InlineData("/api/recurring")]
    [InlineData("/api/tax/profile")]
    public async Task Every_data_endpoint_is_closed_without_signing_in(string url)
    {
        using var api = new LockedApi();

        var res = await Client(api).GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Writing_is_closed_too_not_only_reading()
    {
        using var api = new LockedApi();

        var res = await Client(api).PostAsJsonAsync("/api/transactions", new
        {
            amount = 100m, currency = "PLN", categoryId = 1,
            priority = "Want", frequency = "OneOff",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task The_right_password_opens_the_app_and_the_wrong_one_does_not()
    {
        using var api = new LockedApi();
        var client = Client(api);

        var wrong = await LogIn(client, "hunter2 hunter2");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/categories")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await LogIn(client)).StatusCode);

        // The cookie from the login rides along on the next call — that is the whole session.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/categories")).StatusCode);
    }

    [Fact]
    public async Task An_unknown_address_is_refused_the_same_way_a_wrong_password_is()
    {
        using var api = new LockedApi();

        var res = await Client(api).PostAsJsonAsync("/api/auth/login", new
        {
            email = "someone@else.test", password = OwnerPassword,
        });

        // Same status, and the message must not reveal which half was wrong.
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task The_address_is_matched_regardless_of_case_and_spacing()
    {
        using var api = new LockedApi();
        var client = Client(api);

        var res = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = $"  {TestApiFactory.OwnerEmail.ToUpperInvariant()} ", password = OwnerPassword,
        });

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    [Fact]
    public async Task Logging_out_closes_it_again()
    {
        using var api = new LockedApi();
        var client = Client(api);

        await LogIn(client);
        await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/categories")).StatusCode);
    }

    [Fact]
    public async Task The_status_endpoint_says_an_account_is_needed_before_signing_in()
    {
        using var api = new LockedApi();

        var status = await Client(api).GetFromJsonAsync<Status>("/api/auth/me");

        Assert.True(status!.Required);
        Assert.False(status.Authenticated);
        Assert.Null(status.Email);
    }

    [Fact]
    public async Task Once_signed_in_the_status_endpoint_names_the_account()
    {
        using var api = new LockedApi();
        var client = Client(api);

        await LogIn(client);
        var status = await client.GetFromJsonAsync<Status>("/api/auth/me");

        Assert.True(status!.Authenticated);
        Assert.Equal(TestApiFactory.OwnerEmail, status.Email);
    }

    [Fact]
    public async Task Without_a_configured_account_the_app_stays_open_for_local_work()
    {
        using var api = new TestApiFactory();

        var res = await api.CreateClient().GetAsync("/api/categories");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_takes_effect_and_keeps_this_session_open()
    {
        using var api = new LockedApi();
        var client = Client(api);
        await LogIn(client);

        var change = await client.PostAsJsonAsync("/api/auth/password", new
        {
            currentPassword = OwnerPassword, newPassword = "a longer new one",
        });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        // The device that changed the password is not thrown out by its own action.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/categories")).StatusCode);

        // The old password is gone, the new one works.
        var fresh = Client(api);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LogIn(fresh)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await LogIn(fresh, "a longer new one")).StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_needs_the_current_one()
    {
        using var api = new LockedApi();
        var client = Client(api);
        await LogIn(client);

        var res = await client.PostAsJsonAsync("/api/auth/password", new
        {
            currentPassword = "not it at all", newPassword = "a longer new one",
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_short_new_password_is_refused()
    {
        using var api = new LockedApi();
        var client = Client(api);
        await LogIn(client);

        var res = await client.PostAsJsonAsync("/api/auth/password", new
        {
            currentPassword = OwnerPassword, newPassword = "short",
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// The point of the security stamp: a cookie is good for a month, so without this a
    /// session opened on a device you no longer have could not be ended at all.
    [Fact]
    public async Task Changing_the_password_ends_sessions_opened_elsewhere()
    {
        using var api = new LockedApi();
        var phone = Client(api);
        var laptop = Client(api);
        await LogIn(phone);
        await LogIn(laptop);

        await laptop.PostAsJsonAsync("/api/auth/password", new
        {
            currentPassword = OwnerPassword, newPassword = "a longer new one",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/categories")).StatusCode);
    }

    [Fact]
    public async Task Signing_out_everywhere_ends_every_session_including_this_one()
    {
        using var api = new LockedApi();
        var phone = Client(api);
        var laptop = Client(api);
        await LogIn(phone);
        await LogIn(laptop);

        var res = await laptop.PostAsync("/api/auth/sign-out-everywhere", null);
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await laptop.GetAsync("/api/categories")).StatusCode);

        // And the password still gets you back in — it was the sessions that ended, not the account.
        Assert.Equal(HttpStatusCode.NoContent, (await LogIn(laptop)).StatusCode);
    }

    [Fact]
    public async Task Changing_the_address_changes_what_signs_in()
    {
        using var api = new LockedApi();
        var client = Client(api);
        await LogIn(client);

        var res = await client.PostAsJsonAsync("/api/auth/email", new
        {
            password = OwnerPassword, email = "New@Home.test",
        });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var status = await client.GetFromJsonAsync<Status>("/api/auth/me");
        Assert.Equal("new@home.test", status!.Email);

        var fresh = Client(api);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LogIn(fresh)).StatusCode);
        var withNew = await fresh.PostAsJsonAsync("/api/auth/login", new
        {
            email = "new@home.test", password = OwnerPassword,
        });
        Assert.Equal(HttpStatusCode.NoContent, withNew.StatusCode);
    }

    [Fact]
    public async Task Account_endpoints_are_closed_to_a_stranger()
    {
        using var api = new LockedApi();
        var client = Client(api);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/password", new
            {
                currentPassword = OwnerPassword, newPassword = "a longer new one",
            })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync("/api/auth/sign-out-everywhere", null)).StatusCode);
    }

    /// Guessing has to be expensive. The limit is per address the requests come from, and
    /// the test client is one, so this also proves the limiter is actually wired up.
    [Fact]
    public async Task Guessing_the_password_over_and_over_stops_being_answered()
    {
        using var api = new LockedApi();
        var client = Client(api);

        HttpStatusCode last = HttpStatusCode.OK;
        for (var attempt = 0; attempt < 15; attempt++)
            last = (await LogIn(client, $"wrong guess {attempt}")).StatusCode;

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    private record Status(bool Required, bool Authenticated, string? Email);
}
