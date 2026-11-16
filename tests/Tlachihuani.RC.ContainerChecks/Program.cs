// Tlachihuani — Angela López Mendoza · XAIHT.
// Runs inside the isolated cloud container's network namespace, with no host port.
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;

var origin = "https://127.0.0.1:12493";
var certificate = X509CertificateLoader.LoadCertificateFromFile("/lab/cert.pem");
var expected = SHA256.HashData(certificate.RawData);
var cookies = new CookieContainer();
using var handler = new HttpClientHandler
{
    UseProxy = false, AllowAutoRedirect = false, CookieContainer = cookies,
    ServerCertificateCustomValidationCallback = (_, actual, _, _) => actual is not null
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(actual.RawData), expected)
};
using var client = new HttpClient(handler) { BaseAddress = new Uri(origin), Timeout = TimeSpan.FromSeconds(5) };
var checks = new List<object>();
var failed = 0;
async Task Check(string name, Func<Task> action)
{
    try { await action(); checks.Add(new { name, status = "PASS" }); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; checks.Add(new { name, status = "FAIL", error = error.Message }); Console.WriteLine("FAIL " + name + ": " + error.Message); }
}
static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
async Task<JsonObject> Json(string path)
{
    using var response = await client.GetAsync(path); response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
}
async Task<HttpResponseMessage> Post(string path, object body, string? csrf, string requestOrigin)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
    request.Headers.Add("Origin", requestOrigin);
    if (csrf is not null) request.Headers.Add("X-CSRF-Token", csrf);
    return await client.SendAsync(request);
}
await Check("Linux cloud answers pinned HTTPS health", async () =>
{
    JsonObject? health = null;
    for (var i = 0; i < 30; i++) { try { health = await Json("/healthz"); break; } catch (HttpRequestException) { await Task.Delay(250); } }
    Require(health?["ready"]?.GetValue<bool>() == true, "No ready health response");
    Require(health?["public_remote_enabled"]?.GetValue<bool>() == false, "Internet gate changed");
});
await Check("Container packages bilingual hosted assets", async () =>
{
    foreach (var path in new[] { "/remote.html", "/remote.js", "/remote-transport.js", "/remote-hosted.js", "/remote-i18n.js", "/icons/xt-192.png" })
    { using var response = await client.GetAsync(path); Require(response.IsSuccessStatusCode, "Missing asset " + path); }
    var translation = await client.GetStringAsync("/remote-i18n.js"); Require(translation.Contains("Computadoras"), "Spanish asset absent");
});
string csrf = "";
await Check("Anonymous browser has secure opaque cookie and no identity", async () =>
{
    var bootstrap = await Json("/api/hosted/bootstrap"); csrf = bootstrap["csrf_token"]!.GetValue<string>();
    Require(bootstrap["user"] is null, "Unexpected signed-in user");
    var cookie = cookies.GetCookies(new Uri(origin))["__Host-tlachihuani-hosted"];
    Require(cookie is { Secure: true, HttpOnly: true, Path: "/" }, "Missing secure session cookie");
});
await Check("Container rejects login without CSRF", async () =>
{
    using var response = await Post("/api/hosted/login", new { username = "container-fixture", password = Environment.GetEnvironmentVariable("TL_RC_CONTAINER_PASSWORD") }, null, origin);
    Require(response.StatusCode == HttpStatusCode.Forbidden, "CSRF bypass");
});
await Check("Container authenticates fixture and rotates browser session", async () =>
{
    var before = cookies.GetCookieHeader(new Uri(origin));
    using var response = await Post("/api/hosted/login", new { username = "container-fixture", password = Environment.GetEnvironmentVariable("TL_RC_CONTAINER_PASSWORD") }, csrf, origin);
    Require(response.IsSuccessStatusCode, "Login failed");
    var bootstrap = await Json("/api/hosted/bootstrap"); csrf = bootstrap["csrf_token"]!.GetValue<string>();
    Require(bootstrap["user"]?["username"]?.GetValue<string>() == "container-fixture", "Wrong identity");
    Require(before != cookies.GetCookieHeader(new Uri(origin)), "Session was not rotated");
});
await Check("No connector means no online computer", async () =>
{
    var directory = await Json("/api/hosted/computers");
    Require(directory["computers"] is JsonArray { Count: 0 }, "Unexpected computer");
});
await Check("Foreign Origin cannot mutate container session", async () =>
{
    using var response = await Post("/api/hosted/logout", new { }, csrf, "https://example.invalid");
    Require(response.StatusCode == HttpStatusCode.Forbidden, "Foreign origin accepted");
});
await Check("Container logout removes account authority", async () =>
{
    using var logout = await Post("/api/hosted/logout", new { }, csrf, origin); Require(logout.StatusCode == HttpStatusCode.NoContent, "Logout failed");
    using var directory = await client.GetAsync("/api/hosted/computers"); Require(directory.StatusCode == HttpStatusCode.Unauthorized, "Session survived logout");
});
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { passed = checks.Count - failed, failed, checks }));
return failed == 0 ? 0 : 1;
