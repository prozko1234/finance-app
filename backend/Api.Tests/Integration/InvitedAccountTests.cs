using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FinanceApp.Api.Tests.Integration;

/// The whole road an invited person actually walks, over HTTP: the owner makes a link, the
/// guest registers with it, and then uses the app. The unit tests prove each piece; this
/// proves they are wired to each other — the ownership filter, the provisioning and the
/// cookie all have to agree, and none of them is exercised end to end anywhere else.
public class InvitedAccountTests
{
    private const string OwnerPassword = "correct horse battery";
    private const string GuestEmail = "olya@x.com";
    private const string GuestPassword = "another long one";

    private sealed class LockedApi : TestApiFactory
    {
        protected override string? Password => OwnerPassword;
    }

    private static async Task<HttpClient> OwnerAsync(LockedApi api)
    {
        var client = api.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestApiFactory.OwnerEmail,
            password = OwnerPassword,
        });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        return client;
    }

    private static async Task<string> InviteCodeAsync(HttpClient owner)
    {
        var res = await owner.PostAsJsonAsync("/api/auth/invites", new { note = "Оля" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("code").GetString()!;
    }

    /// A separate client, so the guest carries their own cookie rather than the owner's.
    private static async Task<HttpClient> GuestAsync(LockedApi api, string code)
    {
        var client = api.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/register", new
        {
            code, email = GuestEmail, password = GuestPassword,
        });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        return client;
    }

    [Fact]
    public async Task An_invited_person_lands_in_a_working_empty_app()
    {
        using var api = new LockedApi();
        var owner = await OwnerAsync(api);
        var guest = await GuestAsync(api, await InviteCodeAsync(owner));

        // Signed in as themselves, straight after registering — no second login screen.
        var me = await guest.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.True(me.GetProperty("authenticated").GetBoolean());
        Assert.Equal(GuestEmail, me.GetProperty("email").GetString());
        // Invited, not owner: they cannot invite anybody else.
        Assert.False(me.GetProperty("isOwner").GetBoolean());

        // Provisioned: an account with no categories cannot record a single expense.
        var categories = await guest.GetFromJsonAsync<JsonElement>("/api/categories");
        Assert.NotEmpty(categories.EnumerateArray());

        // And empty of money — none of it is the owner's.
        var transactions = await guest.GetFromJsonAsync<JsonElement>("/api/transactions");
        Assert.Empty(transactions.EnumerateArray());

        // The screen the whole app exists for answers rather than failing.
        var summary = await guest.GetAsync("/api/summary/safe-to-spend");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
    }

    /// The promise made on the registration screen — "власник застосунку теж їх не бачить" —
    /// checked from both sides over real requests.
    [Fact]
    public async Task Neither_account_can_see_the_others_money()
    {
        using var api = new LockedApi();
        var owner = await OwnerAsync(api);
        var guest = await GuestAsync(api, await InviteCodeAsync(owner));

        var ownerCategory = (await owner.GetFromJsonAsync<JsonElement>("/api/categories"))
            .EnumerateArray().First().GetProperty("id").GetInt32();
        var guestCategory = (await guest.GetFromJsonAsync<JsonElement>("/api/categories"))
            .EnumerateArray().First().GetProperty("id").GetInt32();

        await Spend(owner, 250m, ownerCategory);
        await Spend(guest, 40m, guestCategory);

        Assert.Equal(250m, await OnlyAmountAsync(owner));
        Assert.Equal(40m, await OnlyAmountAsync(guest));
    }

    /// Naming a category that belongs to somebody else must not quietly file an expense
    /// against it — the id is guessable, and small integers especially so.
    [Fact]
    public async Task An_expense_cannot_be_filed_against_someone_elses_category()
    {
        using var api = new LockedApi();
        var owner = await OwnerAsync(api);
        var guest = await GuestAsync(api, await InviteCodeAsync(owner));

        var ownerCategory = (await owner.GetFromJsonAsync<JsonElement>("/api/categories"))
            .EnumerateArray().First().GetProperty("id").GetInt32();

        var res = await guest.PostAsJsonAsync("/api/transactions", new
        {
            amount = 10m, currency = "PLN", categoryId = ownerCategory, frequency = "OneOff",
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// An invited account inviting others would turn one link given to one person into an
    /// open door. Refused by the server, whatever the screen decided to show.
    [Fact]
    public async Task An_invited_person_cannot_invite_anybody()
    {
        using var api = new LockedApi();
        var owner = await OwnerAsync(api);
        var guest = await GuestAsync(api, await InviteCodeAsync(owner));

        var created = await guest.PostAsJsonAsync("/api/auth/invites", new { note = "ще хтось" });
        var listed = await guest.GetAsync("/api/auth/invites");

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, listed.StatusCode);
    }

    private static async Task Spend(HttpClient client, decimal amount, int categoryId)
    {
        var res = await client.PostAsJsonAsync("/api/transactions", new
        {
            amount, currency = "PLN", categoryId, frequency = "OneOff",
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    private static async Task<decimal> OnlyAmountAsync(HttpClient client)
    {
        var rows = (await client.GetFromJsonAsync<JsonElement>("/api/transactions"))
            .EnumerateArray().ToList();

        return Assert.Single(rows).GetProperty("amountBase").GetDecimal();
    }
}
