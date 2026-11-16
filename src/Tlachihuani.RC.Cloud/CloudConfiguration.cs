// Tlachihuani — Angela López Mendoza · XAIHT
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tlachihuani.RC.Cloud;

public sealed class CloudConfiguration
{
    public int Schema { get; set; }
    public string SecurityProfile { get; set; } = "";
    public string Origin { get; set; } = "";
    public string CertificatePath { get; set; } = "";
    public string CertificatePassword { get; set; } = "";
    public string AuthenticationMode { get; set; } = "fixture";
    public CloudOidcConfiguration? Oidc { get; set; }
    public string StatePath { get; set; } = "";
    public List<CloudUser> Users { get; set; } = [];
    public List<CloudComputer> Computers { get; set; } = [];
    [JsonIgnore] public Uri PublicOrigin => new(Origin, UriKind.Absolute);
    public const string DevelopmentProfile = "development-loopback";
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 12
    };

    public static CloudConfiguration Load(string path)
    {
        RejectProfilePath(path);
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 262144) throw new InvalidDataException("Development configuration is too large.");
        var config = JsonSerializer.Deserialize<CloudConfiguration>(bytes, Json)
            ?? throw new InvalidDataException("A development configuration is required.");
        if (string.IsNullOrEmpty(config.StatePath)) config.StatePath = Path.GetFullPath(path) + ".state.json";
        config.Validate();
        return config;
    }

    public void Validate()
    {
        if (Schema != 1 || SecurityProfile != DevelopmentProfile)
            throw new InvalidDataException("Only schema 1 and development-loopback are available. Public remote transport remains disabled.");
        if (!Uri.TryCreate(Origin, UriKind.Absolute, out var origin) || origin.Scheme != "https"
            || !IPAddress.TryParse(origin.Host, out var address) || !IPAddress.IsLoopback(address)
            || origin.UserInfo.Length != 0 || origin.AbsolutePath != "/" || origin.Query.Length != 0
            || origin.Fragment.Length != 0 || origin.Port < 1024
            || Origin != origin.GetLeftPart(UriPartial.Authority))
            throw new InvalidDataException("Development origin must be an exact HTTPS loopback-IP origin with a nonprivileged port.");
        if (CertificatePath.Length == 0 || !Path.IsPathFullyQualified(CertificatePath))
            throw new InvalidDataException("An absolute development certificate path is required.");
        RejectProfilePath(CertificatePath);
        if (!Path.IsPathFullyQualified(StatePath) || string.Equals(StatePath, CertificatePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An independent application-owned state path is required.");
        RejectProfilePath(StatePath);
        if (AuthenticationMode is not ("fixture" or "oidc") || (AuthenticationMode == "fixture" && Oidc is not null))
            throw new InvalidDataException("Choose fixture credentials or configured OIDC; never silently fall back.");
        if (AuthenticationMode == "oidc")
        {
            if (Oidc is null) throw new InvalidDataException("OIDC configuration is required.");
            Oidc.Validate(origin);
        }
        if (Users is null || Computers is null || Users.Count is < 1 or > 100 || Computers.Count > 100)
            throw new InvalidDataException("Explicit bounded development users and computers are required.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var user in Users)
        {
            if (user is null || !Id(user.Id) || !ids.Add(user.Id) || string.IsNullOrWhiteSpace(user.Username)
                || user.Username.Length > 80 || user.Username.Any(char.IsControl) || !names.Add(user.Username))
                throw new InvalidDataException("Invalid or duplicate development user.");
            if (AuthenticationMode == "oidc" && (user.OidcIssuer != Oidc!.Authority || string.IsNullOrWhiteSpace(user.OidcSubject)
                || user.OidcSubject.Length > 255 || user.OidcSubject.Any(char.IsControl)
                || !identities.Add(user.OidcIssuer + "\n" + user.OidcSubject)))
                throw new InvalidDataException("OIDC users require unique exact issuer/subject bindings.");
            if (AuthenticationMode == "fixture" && (!Base64(user.PasswordSalt, 16, 64) || !Base64(user.PasswordHash, 32, 32)))
                throw new InvalidDataException("Development passwords require a base64 salt and 32-byte PBKDF2-SHA256 hash.");
        }
        var machines = new HashSet<string>(StringComparer.Ordinal);
        var credentials = new HashSet<string>(StringComparer.Ordinal);
        foreach (var computer in Computers)
        {
            if (computer is null || !Id(computer.InstallationId) || !machines.Add(computer.InstallationId)
                || !Token(computer.ConnectorToken) || !credentials.Add(computer.ConnectorToken)
                || string.IsNullOrWhiteSpace(computer.Name) || computer.Name.Length > 80 || computer.Name.Any(char.IsControl)
                || computer.Accounts is null || computer.Accounts.Count is < 1 or > 100)
                throw new InvalidDataException("Invalid or duplicate development computer.");
            var bindings = new HashSet<string>(StringComparer.Ordinal);
            foreach (var account in computer.Accounts)
                if (account is null || !Id(account.BindingId) || !bindings.Add(account.BindingId)
                    || !ids.Contains(account.OwnerId) || account.Edition is not ("english" or "spanish")
                    || string.IsNullOrWhiteSpace(account.DisplayName) || account.DisplayName.Length > 80 || account.DisplayName.Any(char.IsControl))
                    throw new InvalidDataException("Invalid or duplicate account binding.");
        }
    }

    public X509Certificate2 LoadCertificate() => X509CertificateLoader.LoadPkcs12FromFile(
        CertificatePath, CertificatePassword, OperatingSystem.IsWindows()
            ? X509KeyStorageFlags.MachineKeySet
            : X509KeyStorageFlags.EphemeralKeySet);
    public static bool Id(string? value) => value is { Length: 32 } && value.All(IsHex);
    public static bool Token(string? value) => value is { Length: 64 } && value.All(IsHex);
    private static bool IsHex(char value) => value is >= '0' and <= '9' or >= 'a' and <= 'f';
    private static bool Base64(string value, int min, int max)
    {
        try { var bytes = Convert.FromBase64String(value); return bytes.Length >= min && bytes.Length <= max; }
        catch (FormatException) { return false; }
    }
    internal static void RejectProfilePath(string path)
    {
        var full = Path.GetFullPath(path);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0)
        {
            var profiles = Directory.GetParent(profile)?.FullName;
            if (full.Equals(profile, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(profile.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || (OperatingSystem.IsWindows() && profiles is not null
                    && full.StartsWith(profiles.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Development state and certificates must be outside user profiles.");
        }
        var current = new FileInfo(full) as FileSystemInfo;
        while (current is not null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Development paths must not traverse links or reparse points.");
            current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent;
        }
    }
}

public sealed class CloudUser
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string OidcIssuer { get; set; } = "";
    public string OidcSubject { get; set; } = "";
}
public sealed class CloudComputer
{
    public string InstallationId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ConnectorToken { get; set; } = "";
    public List<CloudAccount> Accounts { get; set; } = [];
}
public sealed class CloudAccount
{
    public string BindingId { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string Edition { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed class CloudOidcConfiguration
{
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string CertificateSha256 { get; set; } = "";
    public void Validate(Uri applicationOrigin)
    {
        if (!Uri.TryCreate(Authority, UriKind.Absolute, out var authority) || authority.Scheme != "https"
            || !IPAddress.TryParse(authority.Host, out var address) || !IPAddress.IsLoopback(address)
            || authority.Host == applicationOrigin.Host || authority.Port < 1024 || authority.UserInfo.Length != 0
            || authority.Query.Length != 0 || authority.Fragment.Length != 0 || Authority.EndsWith('/'))
            throw new InvalidDataException("The OIDC development authority requires HTTPS on a distinct loopback IP, without a trailing slash.");
        if (string.IsNullOrWhiteSpace(ClientId) || ClientId.Length > 255 || ClientId.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(ClientSecret) || ClientSecret.Length > 4096 || !CloudConfiguration.Token(CertificateSha256))
            throw new InvalidDataException("OIDC client credentials and an exact certificate SHA-256 pin are required.");
    }
}
