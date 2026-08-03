using System.Text.Json;

namespace FinanceApp.Api.Endpoints;

/// Tells the native shell whether there is a newer web bundle to run.
///
/// This is what keeps the "жив → муляє → полагодив того ж дня" loop alive once the app is a
/// native one: pushing to main replaces the bundle here, and the phone picks it up on its
/// next launch. Only a change to Swift code needs Xcode again.
///
/// Anonymous on purpose: the bundle is the same public JavaScript already served from
/// wwwroot, and an update check that needed a session could not run before login — which is
/// exactly when a broken build most needs replacing.
public static class AppBundleEndpoints
{
    /// Written by scripts/make-bundle.sh at image build time and copied into wwwroot.
    private const string ManifestPath = "app-bundle/bundle.json";

    public static IEndpointRouteBuilder MapAppBundleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/app-bundle").WithTags("AppBundle").AllowAnonymous();

        // Both verbs: the updater posts device details, and a person checking by hand gets
        // the same answer from a browser.
        group.MapMethods("", ["GET", "POST"], (HttpRequest request, IWebHostEnvironment env) =>
        {
            var manifest = Path.Combine(env.WebRootPath ?? "wwwroot", ManifestPath);
            if (!File.Exists(manifest))
            {
                // No bundle shipped with this image — the app keeps running what it has.
                // 200 with an empty body is what the updater reads as "nothing new"; a 404
                // would be logged as a failure every single launch.
                return Results.Ok(new { });
            }

            var bundle = JsonSerializer.Deserialize<BundleManifest>(
                File.ReadAllText(manifest),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            if (bundle is null) return Results.Ok(new { });

            // Absolute, because the app asks from capacitor://localhost — a relative path
            // would resolve against the bundle inside the app and download nothing.
            var origin = $"{request.Scheme}://{request.Host}";

            return Results.Ok(new
            {
                version = bundle.Version,
                url = $"{origin}/app-bundle/{bundle.File}",
                checksum = bundle.Checksum,
            });
        });

        return app;
    }

    private sealed record BundleManifest(string Version, string Checksum, string File);
}
