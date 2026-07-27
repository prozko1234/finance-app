using System.Net;
using System.Net.Http.Json;

namespace FinanceApp.Api.Tests.Integration;

/// The lock itself. These tests are the reason the app can be given a public URL at all,
/// so they check the door rather than the handle: every data endpoint, not just one.
public class AuthTests
{
    private sealed class LockedApi : TestApiFactory
    {
        protected override string? Password => "correct horse";
    }

    /// Redirects are not followed: an unauthenticated API call must ANSWER 401, and a
    /// client following a redirect would see a 200 and think it was let in.
    private static HttpClient Client(LockedApi api) =>
        api.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
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
    public async Task Every_data_endpoint_is_closed_without_the_password(string url)
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

        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { password = "hunter2" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/categories")).StatusCode);

        var right = await client.PostAsJsonAsync("/api/auth/login", new { password = "correct horse" });
        Assert.Equal(HttpStatusCode.NoContent, right.StatusCode);

        // The cookie from the login rides along on the next call — that is the whole session.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/categories")).StatusCode);
    }

    [Fact]
    public async Task Logging_out_closes_it_again()
    {
        using var api = new LockedApi();
        var client = Client(api);

        await client.PostAsJsonAsync("/api/auth/login", new { password = "correct horse" });
        await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/categories")).StatusCode);
    }

    [Fact]
    public async Task The_status_endpoint_says_a_password_is_needed_before_one_is_given()
    {
        using var api = new LockedApi();

        var status = await Client(api).GetFromJsonAsync<Status>("/api/auth/me");

        Assert.True(status!.Required);
        Assert.False(status.Authenticated);
    }

    [Fact]
    public async Task Without_a_configured_password_the_app_stays_open_for_local_work()
    {
        using var api = new TestApiFactory();

        var res = await api.CreateClient().GetAsync("/api/categories");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    private record Status(bool Required, bool Authenticated);
}
