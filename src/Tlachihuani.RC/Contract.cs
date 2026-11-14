// Tlachihuani — Angela López Mendoza · XAIHT
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tlachihuani.RC;

public static class RcProtocol
{
    public const int Major = 1;
    public const string ServiceName = "Tlachihuani.RemoteController";
    public const string DisplayName = "Remote-Controller";
    public const int DefaultPort = 12481;
    public const int MaxMessageBytes = 65536;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
        WriteIndented = false
    };
    public static readonly string[] Scopes = ["monitor", "chat", "audio", "launch", "stop", "workspace", "schedules", "admin"];
    public static readonly IReadOnlyDictionary<string, string> Operations = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["application.status"] = "monitor", ["application.start"] = "launch", ["application.stop"] = "stop",
        ["conversation.submit"] = "chat", ["conversation.cancel"] = "chat", ["conversation.permission"] = "chat", ["conversation.history"] = "chat",
        ["audio.transcribe"] = "audio"
    };
    public static string Token() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    public static string Id() => Guid.NewGuid().ToString("N");
    public static bool IsId(string? value) => value is { Length: 32 } && Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty;
    public static string RequireId(string? value, string field)
    {
        if (!IsId(value)) throw new RcFault("invalid_request", $"Invalid {field}.", 400);
        return value!;
    }
    public static string Text(string? value, string field, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(c => char.IsControl(c) && c is not ('\n' or '\t')))
            throw new RcFault("invalid_request", $"Invalid {field}.", 400);
        return value.Trim();
    }
    public static string Edition(string? value) => value is "english" or "spanish" ? value : throw new RcFault("invalid_request", "Unknown edition.", 400);
    public static string[] ValidateScopes(string[]? scopes)
    {
        if (scopes is null || scopes.Length < 1 || scopes.Length > Scopes.Length || scopes.Distinct(StringComparer.Ordinal).Count() != scopes.Length || scopes.Any(s => !Scopes.Contains(s, StringComparer.Ordinal)))
            throw new RcFault("invalid_request", "Invalid permission set.", 400);
        return [.. scopes.Order(StringComparer.Ordinal)];
    }
}

public sealed class RcFault(string code, string message, int status = 409) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed record ExecutionCapabilities(bool ExecutionOptions, bool SelfModifyAvailable,
    bool SelfModifyActive, bool SelfModifyLocked, string Model, int WebPort = 0);

public sealed record RcConfiguration
{
    public int Schema { get; init; } = 1;
    public string BindAddress { get; init; } = "127.0.0.1";
    public int Port { get; init; } = RcProtocol.DefaultPort;
    public string PublicOrigin { get; init; } = "http://127.0.0.1:12481";
    public string SecurityProfile { get; init; } = "local-only";
    public string? CertificateThumbprint { get; init; }
    public bool IncludeInStartServices { get; init; }
    public int MaxSessions { get; init; } = 16;
    public int MaxAccountSessions { get; init; } = 4;
    public int SessionMinutes { get; init; } = 15;
    public void Validate()
    {
        if (Schema != 1) throw new RcFault("incompatible_configuration", "Unsupported RC configuration schema.");
        if (!System.Net.IPAddress.TryParse(BindAddress, out var address) || address.Equals(System.Net.IPAddress.Any) || address.Equals(System.Net.IPAddress.IPv6Any))
            throw new RcFault("invalid_configuration", "Select one explicit listener address.");
        if (Port is < 1024 or > 65535 || Port == 12064) throw new RcFault("invalid_configuration", "Invalid RC port; Torrenter's port is reserved.");
        if (!Uri.TryCreate(PublicOrigin, UriKind.Absolute, out var origin) || origin.UserInfo.Length != 0 || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0 || origin.Port != Port)
            throw new RcFault("invalid_configuration", "Public origin must contain only scheme, host and the RC port.");
        if (MaxSessions is < 1 or > 64 || MaxAccountSessions is < 1 or > 16 || MaxAccountSessions > MaxSessions || SessionMinutes is < 1 or > 30)
            throw new RcFault("invalid_configuration", "Invalid session limits.");
        if (SecurityProfile == "local-only")
        {
            if (!System.Net.IPAddress.IsLoopback(address) || !origin.IsLoopback || origin.Scheme != "http" || CertificateThumbprint is not null)
                throw new RcFault("invalid_configuration", "Local-only mode requires a loopback HTTP origin and no certificate.");
        }
        else if (SecurityProfile == "post-quantum-required")
        {
            // No classical TLS fallback is permitted by the approved design.
            throw new RcFault("security_profile_unavailable", "The reviewed ML-KEM-1024 / ML-DSA-87 transport is not installed. Remote exposure remains disabled.");
        }
        else throw new RcFault("invalid_configuration", "Unknown security profile.");
    }
}

public sealed record AccountBinding(string Id, string Edition, string AccountId, string DisplayName, string WindowsSid, string AdapterId, long Revision = 1, bool Enabled = true);
public sealed record DeviceGrant(string Id, string BindingId, string Name, string PublicKey, string[] Scopes, DateTimeOffset EnrolledAt, bool Revoked = false, long Revision = 1);
public sealed record Invitation(string Id, string TokenHash, string BindingId, string[] Scopes, DateTimeOffset ExpiresAt, bool Used = false);
public sealed record Enrollment(string Id, string PollHash, string InvitationId, string Name, string PublicKey, string Fingerprint, DateTimeOffset ExpiresAt, string Status = "pending", string? DeviceId = null);
public sealed record OperationReceipt(string Id, string BindingId, string DeviceId, string Key, string Digest, string Kind, JsonElement Payload, DateTimeOffset CreatedAt, DateTimeOffset Deadline, string State = "accepted", string? AdapterOperationId = null, JsonElement? Result = null, string? Error = null, JsonElement? Progress = null, JsonElement? PendingPermission = null);
public sealed record RcEvent(long Cursor, DateTimeOffset At, string Kind, string? BindingId, string? OperationId, string Outcome);
public sealed record RcSchedule(string Id, string BindingId, string Name, string Operation, JsonElement Payload, string TimeZone, string LocalTime, int[] Days, string MissedRun, bool Enabled, long Revision, DateTimeOffset NextRun, int GraceMinutes = 15, string AuthorDeviceId = "");
public sealed record DurableState
{
    public int Schema { get; init; } = 1;
    public string InstallationId { get; init; } = RcProtocol.Id();
    public long Cursor { get; set; }
    public Dictionary<string, AccountBinding> Bindings { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, DeviceGrant> Devices { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, Invitation> Invitations { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, Enrollment> Enrollments { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, OperationReceipt> Operations { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, RcSchedule> Schedules { get; init; } = new(StringComparer.Ordinal);
    public List<RcEvent> Events { get; init; } = [];
}
