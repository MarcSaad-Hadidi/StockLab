using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using StockLab.Application.Exceptions;
using StockLab.Application.Interfaces;

namespace StockLab.Infrastructure.Identity;

public sealed class GoogleIdentityProvider(IHttpClientFactory clients, IOptions<GoogleAuthOptions> options,
    GoogleIdTokenValidator validator) : IGoogleIdentityProvider
{
    public async Task<GoogleIdentity> ExchangeAsync(string code, string origin, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var client = clients.CreateClient("GoogleOAuth");
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code, ["client_id"] = options.Value.ClientId, ["client_secret"] = options.Value.ClientSecret,
                ["grant_type"] = "authorization_code", ["redirect_uri"] = origin
            });
            using var response = await client.PostAsync("https://oauth2.googleapis.com/token", form, timeout.Token);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                throw new GoogleAuthenticationException("google_invalid_response");
            if (!response.IsSuccessStatusCode) throw new GoogleAuthenticationException("google_unavailable");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("id_token", out var idToken) || idToken.ValueKind != JsonValueKind.String
                || idToken.GetString() is not { Length: > 0 and <= 16384 } token)
                throw new GoogleAuthenticationException("google_invalid_response");
            // Google access/refresh tokens have no purpose in StockLab and are never persisted or returned.
            return await validator.ValidateAsync(token, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new GoogleAuthenticationException("google_unavailable"); }
        catch (HttpRequestException) { throw new GoogleAuthenticationException("google_unavailable"); }
        catch (JsonException) { throw new GoogleAuthenticationException("google_invalid_response"); }
    }
}

public sealed class GoogleIdTokenValidator(IConfigurationManager<OpenIdConnectConfiguration> configuration,
    IOptions<GoogleAuthOptions> options, TimeProvider time)
{
    public async Task<GoogleIdentity> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await configuration.GetConfigurationAsync(cancellationToken).WaitAsync(cancellationToken);
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 16384 };
            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true, RequireSignedTokens = true,
                IssuerSigningKeys = metadata.SigningKeys, ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ValidateIssuer = true, ValidIssuers = ["accounts.google.com", "https://accounts.google.com"],
                ValidateAudience = true, ValidAudience = options.Value.ClientId,
                ValidateLifetime = true, RequireExpirationTime = true, ClockSkew = TimeSpan.FromSeconds(30)
            };
            var principal = handler.ValidateToken(token, parameters, out var validated);
            if (validated is not JwtSecurityToken jwt || !jwt.Payload.ContainsKey("iat")
                || jwt.IssuedAt > time.GetUtcNow().UtcDateTime.AddSeconds(30) || jwt.IssuedAt < DateTime.UnixEpoch
                || jwt.ValidTo <= jwt.IssuedAt
                || principal.FindFirst("azp") is { } authorizedParty && authorizedParty.Value != options.Value.ClientId)
                throw new GoogleAuthenticationException("google_invalid_response");
            var subject = principal.FindFirst("sub")?.Value;
            var email = principal.FindFirst("email")?.Value;
            if (subject is not { Length: > 0 and <= 255 } || subject.Any(char.IsWhiteSpace)
                || email is not { Length: > 0 and <= 254 } || email != email.Trim() || !new EmailAddressAttribute().IsValid(email)
                || principal.FindFirst("email_verified")?.Value is not ("true" or "True"))
                throw new GoogleAuthenticationException("google_invalid_response");
            var displayName = principal.FindFirst("name")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(displayName)) displayName = email.Split('@')[0];
            if (displayName.Length > 100) displayName = displayName[..100];
            return new(subject, email, displayName);
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            // Microsoft's configuration manager caches discovery/keys and refreshes after key rotation.
            configuration.RequestRefresh();
            throw new GoogleAuthenticationException("google_invalid_response");
        }
        catch (SecurityTokenException) { throw new GoogleAuthenticationException("google_invalid_response"); }
        catch (ArgumentException) { throw new GoogleAuthenticationException("google_invalid_response"); }
        catch (InvalidOperationException) { throw new GoogleAuthenticationException("google_unavailable"); }
        catch (IOException) { throw new GoogleAuthenticationException("google_unavailable"); }
    }
}
