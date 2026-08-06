namespace FinanceApp.Domain;

/// Marks a row as belonging to exactly one account.
///
/// The point of a marker rather than a copied property is that the database context can find
/// every one of these by itself: the query filter and the stamping of new rows are applied by
/// walking the model, so adding an entity later cannot forget either. Reads are therefore
/// closed by default — the same bet that <c>FallbackPolicy = RequireAuthenticatedUser</c>
/// makes for endpoints, and for the same reason: the failure this prevents is silent, and
/// shows one person another person's money.
///
/// Deliberately NOT on everything. <see cref="FxRate"/> is shared — the NBP rate for a day is
/// the same fact for everyone, and copying it per account would multiply rows to say one
/// thing. <see cref="Auth.User"/> is the tenant itself, so it cannot belong to one.
///
/// Child rows carry it too, even where the parent already implies it (a bucket's scheme, a
/// skip's recurring expense). It is denormalized on purpose: one uniform filter that cannot
/// be reasoned around beats a shorter schema that needs a join to be safe.
public interface IOwnedByUser
{
    int UserId { get; set; }
}
