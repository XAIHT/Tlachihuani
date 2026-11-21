// Tlachihuani — Angela López Mendoza · XAIHT
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tlachihuani.Tray;

// The xaiht.org link. XT asks xaiht.org for new messages over ordinary HTTPS
// (long polling): one request waits up to 25 seconds and returns at once when a
// message is sent. Only outgoing HTTPS is used, so it works behind any home or
// office router, a hotel Wi-Fi, a phone hotspot or a proxy that allows browsing,
// and the computer opens no port. While XT is asking, xaiht.org shows the
// computer as online. Every message is written to Tlachihuani.log, then
// acknowledged so the website can show that it arrived.
internal sealed record XaihtLinkSettings(string Server, string Key);

internal sealed record XaihtLinkStatus(string State, string Detail, string? Server, string? Computer,
    DateTimeOffset? LastContact, DateTimeOffset? LastMessage, int Received);

internal static class XaihtLinkStore
{
    private static readonly byte[] entropy = Encoding.UTF8.GetBytes("XAIHT.Tlachihuani.XaihtLink.v1");
    internal static string FilePath => Path.Combine(TlachihuaniLog.Folder, "xaiht-link.json");
    internal const string DefaultServer = "https://xaiht.org";

    // The key is protected with Windows DPAPI for the current user; only this
    // Windows account on this computer can read it back.
    internal static void Save(XaihtLinkSettings settings)
    {
        var protectedKey = ProtectedData.Protect(Encoding.UTF8.GetBytes(settings.Key), entropy, DataProtectionScope.CurrentUser);
        var json = new JsonObject { ["server"] = settings.Server, ["key"] = Convert.ToBase64String(protectedKey), ["version"] = 1 };
        Directory.CreateDirectory(TlachihuaniLog.Folder);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(temporary, FilePath, overwrite: true);
    }

    internal static XaihtLinkSettings? Load()
    {
        if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > 16384) return null;
        var json = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? throw new InvalidDataException("xaiht-link.json is not an object.");
        var server = (string?)json["server"] ?? DefaultServer;
        var key = Convert.FromBase64String((string?)json["key"] ?? throw new InvalidDataException("xaiht-link.json has no key."));
        var plain = Encoding.UTF8.GetString(ProtectedData.Unprotect(key, entropy, DataProtectionScope.CurrentUser));
        return new XaihtLinkSettings(NormalizeServer(server) ?? throw new InvalidDataException("xaiht-link.json has an invalid website."), plain);
    }

    internal static void Delete() { if (File.Exists(FilePath)) File.Delete(FilePath); }

    // https only; plain http is accepted for this computer itself, for tests.
    internal static string? NormalizeServer(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) return null;
        if (uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0) return null;
        return uri.GetLeftPart(UriPartial.Authority);
    }

    internal static bool LooksLikeKey(string? key) => key is { Length: >= 40 and <= 200 } && key.StartsWith("xrc_", StringComparison.Ordinal)
        && key.Skip(4).All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

internal sealed class XaihtLink
{
    internal static XaihtLink Shared { get; } = new();
    private const int WaitSeconds = 25;
    private static readonly string version = (typeof(XaihtLink).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0").Split('+')[0];
    private static readonly JsonSerializerOptions pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly object gate = new();
    private readonly HashSet<long> logged = [];
    private readonly Queue<long> loggedOrder = new();
    private CancellationTokenSource restart = new();
    private XaihtLinkStatus status = new("off", "Not connected to xaiht.org.", null, null, null, null, 0);

    internal XaihtLinkStatus Status { get { lock (gate) return status; } }

    // Settings changed (Connect or Disconnect): drop the current request and start again.
    internal void Reconfigure()
    {
        lock (gate) { restart.Cancel(); }
    }

    internal async Task RunAsync(CancellationToken closing)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(WaitSeconds + 20) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Tlachihuani", version));
        var backoff = TimeSpan.FromSeconds(2);
        while (!closing.IsCancellationRequested)
        {
            // Each poll is written only when it brings or reports something new; the pass is
            // closed before every pause so nothing waits unwritten while the link sleeps.
            var pass = Tlachihuani.Contracts.TraceLog.Pass("xaiht.org poll");
            try {
            CancellationToken changed;
            lock (gate)
            {
                if (restart.IsCancellationRequested) { restart.Dispose(); restart = new CancellationTokenSource(); }
                changed = restart.Token;
            }
            using var both = CancellationTokenSource.CreateLinkedTokenSource(closing, changed);
            XaihtLinkSettings? settings;
            try { settings = XaihtLinkStore.Load(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidDataException or CryptographicException)
            {
                Set("error", "The saved xaiht.org key could not be read. Connect again.", null, null);
                pass.Dispose();
                if (!await Pause(Timeout.InfiniteTimeSpan, both.Token, closing)) return;
                continue;
            }
            if (settings is null)
            {
                Set("off", "Not connected to xaiht.org.", null, null);
                pass.Dispose();
                if (!await Pause(Timeout.InfiniteTimeSpan, both.Token, closing)) return;
                continue;
            }
            if (Status.State is "off" or "error") Set("connecting", "Connecting to " + settings.Server + "…", settings.Server, null);
            try
            {
                await PollOnce(http, settings, both.Token);
                backoff = TimeSpan.FromSeconds(2);
            }
            catch (OperationCanceledException) when (changed.IsCancellationRequested || closing.IsCancellationRequested) { }
            catch (RejectedException rejected)
            {
                Set("rejected", rejected.Message, settings.Server, null);
                pass.Dispose();
                if (!await Pause(TimeSpan.FromSeconds(60), both.Token, closing)) return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException or IOException)
            {
                var reason = ex is TaskCanceledException ? "xaiht.org did not answer in time." : ex.Message;
                Set("offline", "Cannot reach " + settings.Server + ": " + reason, settings.Server, Status.Computer);
                pass.Dispose();
                if (!await Pause(backoff, both.Token, closing)) return;
                backoff = TimeSpan.FromSeconds(Math.Min(60, backoff.TotalSeconds * 2));
            }
            } finally { pass.Dispose(); }
        }
    }

    private static async Task<bool> Pause(TimeSpan delay, CancellationToken wake, CancellationToken closing)
    {
        try { await Task.Delay(delay, wake); }
        catch (OperationCanceledException) { }
        return !closing.IsCancellationRequested;
    }

    private async Task PollOnce(HttpClient http, XaihtLinkSettings settings, CancellationToken token)
    {
        // The first request after starting or reconnecting answers at once, so XT
        // shows Online as soon as xaiht.org does; the following ones wait.
        int wait = Status.State == "online" && Status.Server == settings.Server ? WaitSeconds : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{settings.Server}/api/rc/poll?wait={wait}");
        Authorize(request, settings);
        using var response = await http.SendAsync(request, token);
        // Only xaiht.org's own 401 means the key is wrong; a 403 usually comes from
        // a firewall in front of the website (for example a bot check).
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new RejectedException("xaiht.org did not accept this computer's key. Create a new key on xaiht.org and connect again.");
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new HttpRequestException("xaiht.org answered 403 Forbidden; a firewall in front of the website may be blocking XT.");
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"xaiht.org answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(token)) as JsonObject ?? throw new InvalidDataException("xaiht.org sent an unexpected answer.");
        var computer = (string?)body["computer"]?["name"];
        Set("online", "Connected. xaiht.org shows this computer as online.", settings.Server, computer);
        var ids = new List<long>();
        foreach (var item in body["messages"]?.AsArray() ?? [])
        {
            if (item is not JsonObject message || message["id"] is null) continue;
            long id = (long)message["id"]!;
            ids.Add(id);
            if (!Remember(id)) continue;   // delivered again before the last acknowledgement arrived
            var payload = message["payload"];
            string text = payload is null ? "{}" : Readable(payload.ToJsonString(pretty));
            TlachihuaniLog.Info("xaiht.org", $"Message #{id} received from xaiht.org", text);
            lock (gate) status = status with { LastMessage = DateTimeOffset.Now, Received = status.Received + 1 };
        }
        if (ids.Count > 0) await Acknowledge(http, settings, ids, token);
    }

    private static async Task Acknowledge(HttpClient http, XaihtLinkSettings settings, List<long> ids, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.Server}/api/rc/ack");
        Authorize(request, settings);
        var body = new JsonObject { ["ids"] = new JsonArray(ids.Select(id => (JsonNode)id).ToArray()), ["written_to"] = "Tlachihuani.log" };
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
            TlachihuaniLog.Warn("xaiht.org", $"The receipt for message {string.Join(", ", ids)} was not accepted ({(int)response.StatusCode}); xaiht.org will send it again.");
    }

    private static void Authorize(HttpRequestMessage request, XaihtLinkSettings settings)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Key);
        request.Headers.TryAddWithoutValidation("X-Tlachihuani-Computer", Environment.MachineName);
        request.Headers.TryAddWithoutValidation("X-Tlachihuani-Version", version);
    }

    // System.Text.Json writes characters outside the Basic Multilingual Plane
    // (emoji, for example) as \uD83D\uDE00 even with relaxed escaping. Put them
    // back as the characters themselves, so the log shows what was sent; the
    // JSON stays valid. A pair preceded by an escaped backslash is left alone.
    private static readonly Regex surrogatePair = new(@"(?<!\\)((?:\\\\)*)\\u(D[89AB][0-9A-F]{2})\\u(D[C-F][0-9A-F]{2})",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    internal static string Readable(string json) => surrogatePair.Replace(json, match => match.Groups[1].Value
        + (char)Convert.ToInt32(match.Groups[2].Value, 16) + (char)Convert.ToInt32(match.Groups[3].Value, 16));

    private bool Remember(long id)
    {
        lock (gate)
        {
            if (!logged.Add(id)) return false;
            loggedOrder.Enqueue(id);
            while (loggedOrder.Count > 1000) logged.Remove(loggedOrder.Dequeue());
            return true;
        }
    }

    // Log only changes of state, so an unreachable website does not fill the log.
    private void Set(string state, string detail, string? server, string? computer)
    {
        XaihtLinkStatus before;
        lock (gate)
        {
            before = status;
            status = status with
            {
                State = state, Detail = detail, Server = server, Computer = computer ?? (server == status.Server ? status.Computer : null),
                LastContact = state == "online" ? DateTimeOffset.Now : status.LastContact
            };
        }
        if (before.State == state && before.Server == server) return;
        var where = server is null ? "" : $" ({server})";
        switch (state)
        {
            case "online": TlachihuaniLog.Info("xaiht.org", $"Connected to xaiht.org{where}" + (computer is null ? "." : $" as computer \"{computer}\".")); break;
            case "offline": TlachihuaniLog.Warn("xaiht.org", detail); break;
            case "rejected": TlachihuaniLog.Error("xaiht.org", detail + where); break;
            case "error": TlachihuaniLog.Error("xaiht.org", detail); break;
            case "off" when before.State != "off": TlachihuaniLog.Info("xaiht.org", "Not connected to xaiht.org."); break;
        }
    }

    private sealed class RejectedException(string message) : Exception(message);
}
