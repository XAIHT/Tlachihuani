// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (native requests/results contain private conversations; explicit lifecycle logging remains)
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tlachihuani.RC;
using Tlachihuani.Windows;

namespace Tlachihuani.Tray;

// This relay is deliberately separate from inert diagnostic messages. Durable
// admission and permissions belong to RC; delivery leases belong to the website.
internal sealed class XaihtCommands
{
    private static readonly JsonSerializerOptions wireJson = new(RcProtocol.Json)
    { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly ConcurrentDictionary<string, byte> active = new(StringComparer.Ordinal);
    private readonly object admission = new();
    private readonly SemaphoreSlim contentSlots = new(4);
    private readonly object snapshotGate = new();
    private readonly Dictionary<string, Snapshot> snapshots = new(StringComparer.Ordinal);
    private System.Threading.Timer? snapshotCleanup;
    private sealed record Snapshot(byte[] Bytes, string Sha256, string Endpoint, DateTimeOffset Created, DateTimeOffset Used, bool Complete = false);
    internal int ActiveCount => active.Count;
    private readonly Func<JsonObject, CancellationToken, Task<JsonObject>> native;
    private readonly Func<HttpClient> contentClient;
    internal XaihtCommands(Func<JsonObject, CancellationToken, Task<JsonObject>>? call = null, Func<HttpClient>? localContentClient = null)
    {
        native = call ?? RcEndpoint.CallInstalledAsync;
        contentClient = localContentClient ?? (() => new HttpClient(new HttpClientHandler
            { AllowAutoRedirect = false, UseProxy = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30) });
    }
    internal void ClearSnapshots()
    {
        lock (snapshotGate) { snapshots.Clear(); snapshotCleanup?.Dispose(); snapshotCleanup = null; }
    }
    private void PruneSnapshots()
    {
        lock (snapshotGate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var item in snapshots.Where(item => now - item.Value.Used >= TimeSpan.FromMinutes(2)
                || now - item.Value.Created >= TimeSpan.FromMinutes(30)).ToArray()) snapshots.Remove(item.Key);
        }
    }
    private Snapshot? FindSnapshot(string key, string endpoint, int offset)
    {
        lock (snapshotGate)
        {
            PruneSnapshots();
            if (snapshots.TryGetValue(key, out var snapshot) && snapshot.Endpoint == endpoint) return snapshot;
            if (offset != 0) throw new RcFault("response_snapshot_expired", "The full-answer snapshot expired or its connection changed. Reload the full answer from its beginning.", 409);
            snapshots.Remove(key); return null;
        }
    }
    private Snapshot SaveSnapshot(string key, string endpoint, byte[] bytes)
    {
        lock (snapshotGate)
        {
            PruneSnapshots();
            if (snapshots.TryGetValue(key, out var existing) && existing.Endpoint == endpoint) return existing;
            while (snapshots.Count >= 4 || snapshots.Values.Sum(item => item.Bytes.Length) + bytes.Length > 32 * 1024 * 1024)
            {
                var finished = snapshots.Where(item => item.Value.Complete).OrderBy(item => item.Value.Used).FirstOrDefault();
                if (finished.Key is null) throw new RcFault("response_busy", "Wait for another full answer to finish loading.", 429);
                snapshots.Remove(finished.Key);
            }
            var now = DateTimeOffset.UtcNow;
            var snapshot = new Snapshot(bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)), endpoint, now, now);
            snapshots[key] = snapshot;
            snapshotCleanup ??= new System.Threading.Timer(_ => PruneSnapshots(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            return snapshot;
        }
    }

    internal void Receive(HttpClient http, XaihtLinkSettings settings, XaihtIdentity identity, JsonArray? items,
        CancellationToken changed, CancellationToken closing)
    {
        // Controls must stay deliverable while conversations wait for approval.
        // A full normal lane never stops us inspecting later control envelopes.
        foreach (var item in (items ?? []).OrderByDescending(item => item is JsonObject envelope
            && envelope["payload"] is JsonObject payload ? Priority(Text(payload["operation"])) : 0))
        {
            if (item is not JsonObject envelope || !PositiveId(envelope["id"], out var id)
                || Text(envelope["delivery_id"]) is not string delivery || !RcProtocol.IsId(delivery)
                || envelope["payload"] is not JsonObject command
                || Text(command["request_id"]) is not string requestId || !RcProtocol.IsId(requestId)) continue;
            var key = identity.ConnectionId + ":" + id + ":" + delivery;
            var operation = Text(command["operation"]);
            var lane = IsControl(operation) ? (byte)1 : IsDiscovery(operation) ? (byte)2 : (byte)0;
            lock (admission)
            {
                var maximum = lane switch { 1 => 8, 2 => 4, _ => 32 };
                if (active.Values.Count(value => value == lane) >= maximum) continue;
                if (!active.TryAdd(key, lane)) continue;
            }
            _ = Run(http, settings, identity, id, delivery, requestId, command.DeepClone().AsObject(), key, changed, closing);
        }
    }
    private static bool IsControl(string? operation) => operation is "conversation.cancel" or "conversation.permission";
    private static bool IsDiscovery(string? operation) => operation is "accounts.list" or "application.status";
    private static int Priority(string? operation) => IsControl(operation) ? 2 : IsDiscovery(operation) ? 1 : 0;
    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static bool PositiveId(JsonNode? node, out long result)
    {
        result = 0;
        if (node is not JsonValue value) return false;
        if (!value.TryGetValue<long>(out result))
        {
            if (!value.TryGetValue<int>(out var small)) return false;
            result = small;
        }
        return result is > 0 and <= 9007199254740991;
    }

    private async Task Run(HttpClient http, XaihtLinkSettings settings, XaihtIdentity identity, long id, string delivery,
        string requestId, JsonObject command, string key, CancellationToken changed, CancellationToken closing)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(changed, closing);
        var token = lifetime.Token;
        long sequence = 0;
        JsonObject? observed = null;
        async Task<JsonObject> RecoveringCall(JsonObject localRequest)
        {
            while (true)
            {
                try { return await Call(localRequest, token); }
                catch (Exception ex) when (!token.IsCancellationRequested && ex is IOException or OperationCanceledException or System.ComponentModel.Win32Exception)
                {
                    // A lost pipe reply cannot prove non-execution. Keep the lease
                    // alive and reconcile the same durable request, never create a
                    // second prompt or turn a running prompt into a false failure.
                    var snapshot = observed?.DeepClone().AsObject() ?? new JsonObject();
                    snapshot["relay_state"] = "reconnecting";
                    if (!await Publish(http, settings, id, delivery, requestId, ++sequence, true, snapshot, null, token))
                        throw new LeaseLostException();
                    await Task.Delay(TimeSpan.FromSeconds(3), token);
                }
            }
        }
        try
        {
            var request = Local("xaiht-command", identity);
            request["command"] = command;
            var result = await RecoveringCall(request);
            if ((string?)command["operation"] == "response.content")
                result = await Content(identity, command, result, token);
            while (!token.IsCancellationRequested)
            {
                observed = result;
                var receipt = result["operation"] as JsonObject;
                var running = (string?)receipt?["state"] is "accepted" or "running";
                if (!await Publish(http, settings, id, delivery, requestId, ++sequence, running, result, null, token)) return;
                if (!running) return;
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                var check = Local("xaiht-operation", identity);
                check["binding_id"] = (string?)command["binding_id"];
                check["operation_id"] = (string?)receipt!["id"];
                result = await RecoveringCall(check);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (LeaseLostException) { }
        catch (Exception ex) when (ex is RcFault or Tlachihuani.Contracts.ProtocolException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or HttpRequestException or JsonException or InvalidOperationException or ArgumentException or OperationCanceledException)
        {
            var code = ex is RcFault fault ? fault.Code : "bridge_unavailable";
            var message = ex is RcFault known ? known.Message : "The local Tlamatini connection was interrupted. Check XT and the selected running account; work already dispatched is not replayed.";
            try { await Publish(http, settings, id, delivery, requestId, ++sequence, false, null,
                new JsonObject { ["code"] = code, ["message"] = message }, token); }
            catch (Exception failure) when (failure is HttpRequestException or IOException or OperationCanceledException) { }
            TlachihuaniLog.Warn("xaiht.org", $"Command #{id}: {code}. No prompt or answer was written to the log.");
        }
        finally { active.TryRemove(key, out _); }
    }

    private async Task<JsonObject> Call(JsonObject request, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        return await native(request, deadline.Token);
    }
    private static JsonObject Local(string op, XaihtIdentity identity) => new()
    { ["op"] = op, ["identity"] = JsonSerializer.SerializeToNode(identity, RcProtocol.Json) };

    private static async Task<bool> Publish(HttpClient http, XaihtLinkSettings settings, long id, string delivery,
        string requestId, long sequence, bool running, JsonObject? result, JsonObject? error, CancellationToken token)
    {
        var value = new JsonObject { ["id"] = id, ["delivery_id"] = delivery, ["request_id"] = requestId,
            ["sequence"] = sequence, ["status"] = running ? "running" : "completed", ["ok"] = error is null };
        if (result is not null) value["result"] = result.DeepClone();
        if (error is not null) value["error"] = error.DeepClone();
        var json = value.ToJsonString(wireJson);
        if (Encoding.UTF8.GetByteCount(json) > 128 * 1024) throw new RcFault("result_too_large", "Response exceeds the relay envelope limit; use its full response ticket.", 413);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, settings.Server + "/api/rc/result");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Key);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Unauthorized or HttpStatusCode.Gone) return false;
                if (!response.IsSuccessStatusCode) throw new HttpRequestException("Command result was not accepted: " + (int)response.StatusCode);
                return true;
            }
            catch (Exception ex) when (attempt < 3 && !token.IsCancellationRequested && ex is HttpRequestException or IOException or OperationCanceledException)
            { await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), token); }
        }
    }

    private async Task<JsonObject> Content(XaihtIdentity identity, JsonObject command, JsonObject authorization, CancellationToken token)
    {
        if (!await contentSlots.WaitAsync(0, token)) throw new RcFault("response_busy", "Wait for another full answer to finish loading.", 429);
        try { return await ReadContent(identity, command, authorization, token); }
        finally { contentSlots.Release(); }
    }

    private async Task<JsonObject> ReadContent(XaihtIdentity identity, JsonObject command, JsonObject authorization, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        token = deadline.Token;
        var binding = (string?)command["binding_id"] ?? "";
        var payload = command["payload"]!.AsObject();
        var offset = (int)payload["offset"]!;
        var endpoint = (string?)authorization["endpoint"] ?? throw new RcFault("invalid_response", "Content authorization is missing.");
        var uri = new Uri(endpoint);
        if (uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.Port < 1024 || uri.AbsolutePath != "/agent/remote-controller/response-content/")
            throw new RcFault("invalid_response", "Invalid local content endpoint.");
        var snapshotKey = identity.ConnectionId + ":" + binding + ":" + RcProtocol.Hash((string?)payload["ticket"] ?? "");
        var snapshot = FindSnapshot(snapshotKey, endpoint, offset);
        using var client = contentClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("X-Tlachihuani-Binding", binding);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) throw new RcFault("response_unavailable", "The full answer is unavailable or its ticket expired.", (int)response.StatusCode);
        if (response.Content.Headers.ContentType?.MediaType != "application/json") throw new RcFault("invalid_response", "The full answer is not JSON.");
        // Every chunk still validates the actual signed ticket against Tlamatini.
        // The endpoint may issue freshly timestamped file links, so only the first
        // successful body is retained; later bodies must never change its digest.
        var fresh = snapshot is null ? await ReadBounded(response.Content, 8 * 1024 * 1024, token) : null;
        // Revalidate the local grant and endpoint after reading, before any bytes
        // leave the machine. Revocation or account/adapter changes stop delivery.
        var recheck = Local("xaiht-content-authorize", identity);
        recheck["binding_id"] = binding; recheck["ticket"] = (string?)payload["ticket"];
        if ((string?)(await Call(recheck, token))["endpoint"] != endpoint) throw new RcFault("account_changed", "The account connection changed.");
        token.ThrowIfCancellationRequested();
        if (snapshot is null)
        {
            using var parsed = JsonDocument.Parse(fresh!);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw new RcFault("invalid_response", "The full answer must be a JSON object.");
            snapshot = SaveSnapshot(snapshotKey, endpoint, fresh!);
        }
        var bytes = snapshot.Bytes;
        if (offset > bytes.Length) throw new RcFault("invalid_request", "Response offset exceeds its length.", 400);
        var count = Math.Min(24576, bytes.Length - offset);
        lock (snapshotGate)
            if (snapshots.TryGetValue(snapshotKey, out var current) && ReferenceEquals(current.Bytes, bytes))
                snapshots[snapshotKey] = current with { Used = DateTimeOffset.UtcNow, Complete = offset + count == bytes.Length || current.Complete };
        return new JsonObject { ["encoding"] = "base64", ["data"] = Convert.ToBase64String(bytes, offset, count),
            ["offset"] = offset, ["next_offset"] = offset + count, ["total"] = bytes.Length,
            ["sha256"] = snapshot.Sha256, ["done"] = offset + count == bytes.Length };
    }

    internal static async Task<byte[]> ReadBounded(HttpContent content, int maximum, CancellationToken token)
    {
        if (content.Headers.ContentLength is long length && length > maximum) throw new InvalidDataException("Response exceeds its size limit.");
        await using var stream = await content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var block = new byte[16384];
        while (true)
        {
            var read = await stream.ReadAsync(block, token);
            if (read == 0) break;
            if (buffer.Length + read > maximum) throw new InvalidDataException("Response exceeds its size limit.");
            await buffer.WriteAsync(block.AsMemory(0, read), token);
        }
        return buffer.ToArray();
    }
    private sealed class LeaseLostException : Exception;
}
