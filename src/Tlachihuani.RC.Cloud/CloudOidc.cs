// Tlachihuani — Angela López Mendoza · XAIHT
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Tlachihuani.RC.Cloud;

internal static class CloudOidc
{
    internal const string Scheme = "hosted-oidc";
    internal const string CallbackPath = "/signin-oidc";
    private const string SessionProperty = "tlachihuani.pre-auth-session";
    private const string ValidatedIssuerProperty = "tlachihuani.validated-issuer";

    internal static void Add(WebApplicationBuilder builder, CloudConfiguration configuration)
    {
        if (configuration.AuthenticationMode != "oidc") return;
        var oidc = configuration.Oidc!;
        // State/correlation protection is process-local in the isolated lab. This
        // explicitly avoids ASP.NET's default user-profile key repository.
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultChallengeScheme = Scheme;
            options.DefaultSignInScheme = "oidc-unused";
        }).AddCookie("oidc-unused", options =>
        {
            options.Cookie.Name = "__Host-tlachihuani-oidc-unused";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        }).AddOpenIdConnect(Scheme, options =>
        {
            options.Authority = oidc.Authority;
            options.ClientId = oidc.ClientId;
            options.ClientSecret = oidc.ClientSecret;
            options.CallbackPath = CallbackPath;
            options.RequireHttpsMetadata = true;
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.ResponseMode = OpenIdConnectResponseMode.Query;
            options.UsePkce = true;
            options.SaveTokens = false;
            options.GetClaimsFromUserInfoEndpoint = false;
            options.MapInboundClaims = false;
            options.DisableTelemetry = true;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(5);
            options.BackchannelTimeout = TimeSpan.FromSeconds(10);
            options.BackchannelHttpHandler = new PinnedAuthorityHandler(oidc);
            options.CorrelationCookie.Name = "__Host-rc-oidc-correlation.";
            options.CorrelationCookie.Path = "/";
            options.CorrelationCookie.HttpOnly = true;
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
            options.CorrelationCookie.SameSite = SameSiteMode.None;
            options.NonceCookie.Name = "__Host-rc-oidc-nonce.";
            options.NonceCookie.Path = "/";
            options.NonceCookie.HttpOnly = true;
            options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
            options.NonceCookie.SameSite = SameSiteMode.None;
            options.ProtocolValidator.RequireNonce = true;
            options.ProtocolValidator.RequireState = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = oidc.Authority,
                ValidateAudience = true, ValidAudience = oidc.ClientId,
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.EcdsaSha256]
            };
            options.Events = new OpenIdConnectEvents
            {
                OnRedirectToIdentityProvider = context =>
                {
                    if (!AllowedEndpoint(context.ProtocolMessage.IssuerAddress, oidc))
                        throw new CloudFault("invalid_identity_provider", 503);
                    context.ProtocolMessage.RedirectUri = configuration.Origin + CallbackPath;
                    if (context.Properties.Items.TryGetValue("tlachihuani.ui-language", out var language) && language is "en" or "es")
                        context.ProtocolMessage.UiLocales = language;
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    // Default OIDC claim actions may remove the iss claim. Preserve
                    // the issuer from the signature-validated SecurityToken instead.
                    // This does not create a session before nonce/correlation checks.
                    if (context.SecurityToken.Issuer != oidc.Authority)
                    { context.Fail("invalid_identity_issuer"); return Task.CompletedTask; }
                    context.Properties ??= new AuthenticationProperties();
                    context.Properties.Items[ValidatedIssuerProperty] = context.SecurityToken.Issuer;
                    return Task.CompletedTask;
                },
                OnTicketReceived = context =>
                {
                    var sessions = context.HttpContext.RequestServices.GetRequiredService<CloudSessions>();
                    var relay = context.HttpContext.RequestServices.GetRequiredService<CloudRelay>();
                    string? previousToken = null;
                    context.Properties?.Items.TryGetValue(SessionProperty, out previousToken);
                    var previous = sessions.Find(previousToken);
                    string? issuer = null;
                    context.Properties?.Items.TryGetValue(ValidatedIssuerProperty, out issuer);
                    var subject = context.Principal?.FindFirst("sub")?.Value;
                    var user = configuration.Users.FirstOrDefault(user => user.OidcIssuer == issuer && user.OidcSubject == subject);
                    if (previous is null || issuer != oidc.Authority || string.IsNullOrEmpty(subject) || user is null)
                    {
                        context.HandleResponse();
                        context.Response.Redirect("/remote.html?auth_error=identity_not_linked");
                        return Task.CompletedTask;
                    }
                    // This event runs after the handler's token, nonce and correlation
                    // checks. Do not issue a cloud session in OnTokenValidated.
                    var session = sessions.Create(user);
                    sessions.Remove(previous.Token);
                    relay.CloseSession(previous.Token);
                    CloudWeb.Cookie(context.HttpContext, session);
                    context.HandleResponse(); // Never issue the unused framework cookie or browser tokens.
                    context.Response.Redirect("/remote.html");
                    return Task.CompletedTask;
                },
                OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    context.Response.Redirect("/remote.html?auth_error=signin_failed");
                    return Task.CompletedTask;
                }
            };
        });
    }

    internal static void Map(WebApplication app, CloudConfiguration configuration, CloudSessions sessions)
    {
        app.MapGet("/auth/login", async (HttpContext context) =>
        {
            if (configuration.AuthenticationMode != "oidc") throw new CloudFault("oidc_unavailable", 409);
            var session = sessions.Find(context.Request.Cookies[CloudSessions.CookieName]);
            if (session is null) { session = sessions.Create(null); CloudWeb.Cookie(context, session); }
            var language = context.Request.Query["language"].ToString();
            if (language.Length > 0 && language is not ("en" or "es")) throw new CloudFault("invalid_language");
            var properties = new AuthenticationProperties { RedirectUri = "/remote.html" };
            properties.Items["tlachihuani.ui-language"] = language.Length == 0 ? "en" : language;
            properties.Items[SessionProperty] = session.Token;
            await context.ChallengeAsync(Scheme, properties);
        });
    }

    private static bool AllowedEndpoint(string? value, CloudOidcConfiguration configuration)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        var authority = new Uri(configuration.Authority);
        return uri.Scheme == "https" && uri.GetLeftPart(UriPartial.Authority) == authority.GetLeftPart(UriPartial.Authority)
            && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
    }

    private sealed class PinnedAuthorityHandler : DelegatingHandler
    {
        private readonly CloudOidcConfiguration configuration;
        internal PinnedAuthorityHandler(CloudOidcConfiguration configuration)
        {
            this.configuration = configuration;
            InnerHandler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                UseCookies = false,
                ServerCertificateCustomValidationCallback = (_, certificate, _, errors) => certificate is not null
                    && (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) == 0
                    && certificate.NotBefore.ToUniversalTime() <= DateTime.UtcNow && certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow
                    && CryptographicOperations.FixedTimeEquals(SHA256.HashData(certificate.RawData), Convert.FromHexString(configuration.CertificateSha256))
            };
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!AllowedEndpoint(request.RequestUri?.AbsoluteUri, configuration))
                throw new HttpRequestException("The identity provider endpoint is outside the configured loopback authority.");
            return base.SendAsync(request, cancellationToken);
        }
    }
}
