using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FinanceApp.Api.Tests.Integration;

/// The second door. A device token is a password that never expires and lives on a phone,
/// so these tests care mostly about how it stops working: revoked, or outlived by the
/// account state it was issued against.
public class DeviceTokenTests
{
    private const string OwnerPassword = "correct horse battery";

    private sealed class LockedApi : TestApiFactory
    {
        protected override string? Password => OwnerPassword;
    }

    private static HttpClient Client(LockedApi api) =>
        api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private sealed record IssuedToken(int Id, string Name, string Token);

    private static async Task<IssuedToken> IssueAsync(HttpClient client, string name = "iPhone")
    {
        var res = await client.PostAsJsonAsync("/api/auth/device-tokens", new
        {
            email = TestApiFactory.OwnerEmail,
            password = OwnerPassword,
            name,
        });

        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<IssuedToken>())!;
    }

    private static HttpClient WithToken(LockedApi api, string token)
    {
        var client = Client(api);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> LogIn(HttpClient client) =>
        client.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestApiFactory.OwnerEmail,
            password = OwnerPassword,
        });

    [Fact]
    public async Task A_token_opens_the_same_doors_as_a_cookie()
    {
        using var api = new LockedApi();
        var issued = await IssueAsync(Client(api));

        var res = await WithToken(api, issued.Token).GetAsync("/api/summary/safe-to-spend");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task The_secret_is_returned_once_and_never_listed_again()
    {
        using var api = new LockedApi();
        var issued = await IssueAsync(Client(api));

        var listed = await WithToken(api, issued.Token).GetStringAsync("/api/auth/device-tokens");

        Assert.Contains("iPhone", listed);
        // The whole point of storing a hash: even the owner cannot read the secret back.
        Assert.DoesNotContain(issued.Token, listed);
    }

    [Fact]
    public async Task A_wrong_password_buys_no_token()
    {
        using var api = new LockedApi();

        var res = await Client(api).PostAsJsonAsync("/api/auth/device-tokens", new
        {
            email = TestApiFactory.OwnerEmail,
            password = "not the password",
            name = "iPhone",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task A_nameless_device_is_a_bad_request_not_a_failed_login()
    {
        using var api = new LockedApi();

        var res = await Client(api).PostAsJsonAsync("/api/auth/device-tokens", new
        {
            email = TestApiFactory.OwnerEmail,
            password = OwnerPassword,
            name = "   ",
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    public async Task An_unknown_token_is_not_let_in(string token)
    {
        using var api = new LockedApi();

        var res = await WithToken(api, token).GetAsync("/api/summary/safe-to-spend");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Revoking_a_device_shuts_it_out()
    {
        using var api = new LockedApi();
        var issued = await IssueAsync(Client(api));
        var phone = WithToken(api, issued.Token);

        var revoked = await phone.DeleteAsync($"/api/auth/device-tokens/{issued.Id}");

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/summary/safe-to-spend")).StatusCode);
    }

    [Fact]
    public async Task Revoking_one_device_leaves_the_others_alone()
    {
        using var api = new LockedApi();
        var phoneToken = await IssueAsync(Client(api), "iPhone");
        var widgetToken = await IssueAsync(Client(api), "Віджет");

        await WithToken(api, phoneToken.Token).DeleteAsync($"/api/auth/device-tokens/{phoneToken.Id}");

        var res = await WithToken(api, widgetToken.Token).GetAsync("/api/summary/safe-to-spend");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Signing_out_everywhere_reaches_the_phone_too()
    {
        using var api = new LockedApi();
        var issued = await IssueAsync(Client(api));

        // "Everywhere" that spared the device most likely to be lost would be a lie — which
        // is exactly why the token carries the security stamp it was issued against.
        var browser = Client(api);
        await LogIn(browser);
        (await browser.PostAsync("/api/auth/sign-out-everywhere", null)).EnsureSuccessStatusCode();

        var res = await WithToken(api, issued.Token).GetAsync("/api/summary/safe-to-spend");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_ends_the_phone_session_as_well()
    {
        using var api = new LockedApi();
        var issued = await IssueAsync(Client(api));

        var browser = Client(api);
        await LogIn(browser);
        var changed = await browser.PostAsJsonAsync("/api/auth/password", new
        {
            currentPassword = OwnerPassword,
            newPassword = "a much longer new one",
        });
        changed.EnsureSuccessStatusCode();

        var res = await WithToken(api, issued.Token).GetAsync("/api/summary/safe-to-spend");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task The_app_can_tell_it_is_signed_in_with_a_token()
    {
        using var api = new LockedApi();
        var issued = await IssueAsync(Client(api));

        // /api/auth/me is anonymous, so nothing authorizes it into reading the bearer token —
        // this is the test that the scheme selector, not a policy, is what identifies callers.
        var me = await WithToken(api, issued.Token).GetStringAsync("/api/auth/me");

        Assert.Contains("\"authenticated\":true", me);
        Assert.Contains(TestApiFactory.OwnerEmail, me);
    }

    [Fact]
    public async Task Listing_devices_needs_a_way_in_of_its_own()
    {
        using var api = new LockedApi();

        var res = await Client(api).GetAsync("/api/auth/device-tokens");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
