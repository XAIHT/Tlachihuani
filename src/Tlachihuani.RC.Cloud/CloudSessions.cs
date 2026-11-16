// Tlachihuani — Angela López Mendoza · XAIHT
using System.Security.Cryptography;
using System.Text;

namespace Tlachihuani.RC.Cloud;

public sealed record CloudSession(string Token, string CsrfToken, CloudUser? User, DateTimeOffset ExpiresAt);

public sealed class CloudSessions(CloudConfiguration configuration)
{
    public const string CookieName = "__Host-tlachihuani-hosted";
    private readonly object gate = new();
    private readonly Dictionary<string, CloudSession> sessions = new(StringComparer.Ordinal);
    private readonly byte[] dummySalt = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] DummyHash = new byte[32];
    public CloudSession Create(CloudUser? user)
    {
        lock (gate)
        {
            Sweep();
            if (sessions.Count >= 512) throw new CloudFault("session_limit", 429);
            var session = new CloudSession(Secret(), Secret(), user,
                DateTimeOffset.UtcNow.Add(user is null ? TimeSpan.FromMinutes(10) : TimeSpan.FromHours(8)));
            sessions.Add(session.Token, session);
            return session;
        }
    }
    public CloudSession? Find(string? token)
    {
        if (!CloudConfiguration.Token(token)) return null;
        lock (gate)
        {
            if (!sessions.TryGetValue(token!, out var session)) return null;
            if (session.ExpiresAt > DateTimeOffset.UtcNow) return session;
            sessions.Remove(token!);
            return null;
        }
    }
    public void Remove(string token) { lock (gate) sessions.Remove(token); }
    public CloudUser VerifyPassword(string username, string password)
    {
        if (username.Length is < 1 or > 80 || password.Length is < 1 or > 1024)
            throw new CloudFault("invalid_credentials", 401);
        var user = configuration.Users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        var salt = user is null ? dummySalt : Convert.FromBase64String(user.PasswordSalt);
        var expected = user is null ? DummyHash : Convert.FromBase64String(user.PasswordHash);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32);
        var valid = CryptographicOperations.FixedTimeEquals(actual, expected);
        CryptographicOperations.ZeroMemory(actual);
        if (!valid || user is null) throw new CloudFault("invalid_credentials", 401);
        return user;
    }
    public static bool SameSecret(string? actual, string? expected) => actual is not null && expected is not null
        && actual.Length == expected.Length
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
    public static string Secret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    private void Sweep()
    {
        foreach (var key in sessions.Where(pair => pair.Value.ExpiresAt <= DateTimeOffset.UtcNow).Select(pair => pair.Key).ToArray())
            sessions.Remove(key);
    }
}

public sealed class CloudFault(string code, int status = 400) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
