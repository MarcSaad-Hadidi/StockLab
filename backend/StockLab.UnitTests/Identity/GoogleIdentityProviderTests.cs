using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using StockLab.Application.Exceptions;
using StockLab.Infrastructure.Identity;

namespace StockLab.UnitTests.Identity;

public sealed class GoogleIdentityProviderTests
{
    private const string ClientId = "123-test.apps.googleusercontent.com";

    [Fact]
    public async Task Real_validator_accepts_only_cryptographically_verified_google_claims_and_extracts_identity()
    {
        using var fixture = new SignedTokenFixture();
        var result = await fixture.Validator.ValidateAsync(fixture.Token(), default);
        Assert.Equal("google-subject-123", result.Subject);
        Assert.Equal("person@external.test", result.Email);
        Assert.Equal("Google Person", result.DisplayName);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("future-issued")]
    [InlineData("missing-issued")]
    [InlineData("missing-subject")]
    [InlineData("unverified-email")]
    [InlineData("missing-email")]
    [InlineData("invalid-email")]
    [InlineData("different-signing-key")]
    [InlineData("unsigned")]
    [InlineData("hmac")]
    [InlineData("malformed")]
    [InlineData("authorized-party")]
    public async Task Real_validator_rejects_invalid_id_token_without_any_identity(string fault)
    {
        using var fixture = new SignedTokenFixture();
        var token = fault == "malformed" ? "test.fake.google" : fixture.Token(fault);
        var failure = await Assert.ThrowsAsync<GoogleAuthenticationException>(() => fixture.Validator.ValidateAsync(token, default));
        Assert.Equal("google_invalid_response", failure.Error);
    }

    [Fact]
    public async Task Real_code_exchange_sends_origin_and_secret_only_to_google_and_validates_id_token()
    {
        using var fixture = new SignedTokenFixture();
        var handler = new TestOnlyTokenHandler(JsonSerializer.Serialize(new
        { id_token = fixture.Token(), access_token = "discard-google-access", refresh_token = "discard-google-refresh" }));
        var provider = new GoogleIdentityProvider(new TestOnlyClientFactory(handler), Config(), fixture.Validator);
        var identity = await provider.ExchangeAsync("private-code", "https://frontend.example.test", default);
        Assert.Equal("google-subject-123", identity.Subject);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://oauth2.googleapis.com/token", handler.Uri);
        Assert.Equal("application/x-www-form-urlencoded", handler.ContentType);
        var fields = handler.Body!.Split('&').Select(value => value.Split('=', 2)).ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));
        Assert.Equal("private-code", fields["code"]);
        Assert.Equal(ClientId, fields["client_id"]);
        Assert.Equal("test-client-secret", fields["client_secret"]);
        Assert.Equal("authorization_code", fields["grant_type"]);
        Assert.Equal("https://frontend.example.test", fields["redirect_uri"]);
    }

    [Theory]
    [InlineData(400, "{}", "google_invalid_response")]
    [InlineData(401, "{}", "google_invalid_response")]
    [InlineData(429, "{}", "google_unavailable")]
    [InlineData(503, "{}", "google_unavailable")]
    [InlineData(302, "{}", "google_unavailable")]
    [InlineData(200, "{\"access_token\":\"token-only-is-not-login\"}", "google_invalid_response")]
    [InlineData(200, "{\"id_token\":\"fake-google-identity\"}", "google_invalid_response")]
    [InlineData(200, "not-json", "google_invalid_response")]
    [InlineData(200, "[]", "google_invalid_response")]
    [InlineData(200, "null", "google_invalid_response")]
    public async Task Code_exchange_never_uses_access_tokens_or_provider_failure_as_identity(int status, string body, string expected)
    {
        using var fixture = new SignedTokenFixture();
        var provider = new GoogleIdentityProvider(new TestOnlyClientFactory(new TestOnlyTokenHandler(body, (HttpStatusCode)status)), Config(), fixture.Validator);
        var failure = await Assert.ThrowsAsync<GoogleAuthenticationException>(() => provider.ExchangeAsync("code", "https://frontend.example.test", default));
        Assert.Equal(expected, failure.Error);
    }

    [Theory]
    [InlineData("https://frontend.example.test", false, true)]
    [InlineData("http://localhost:5189", true, true)]
    [InlineData("http://127.0.0.1:5189", true, true)]
    [InlineData("http://localhost:5189", false, false)]
    [InlineData("http://frontend.example.test", true, false)]
    [InlineData("https://frontend.example.test/", false, false)]
    [InlineData("https://frontend.example.test/path", false, false)]
    [InlineData("https://frontend.example.test?value=1", false, false)]
    [InlineData("https://user:secret@frontend.example.test", false, false)]
    [InlineData(" https://frontend.example.test", false, false)]
    public void Configuration_accepts_only_canonical_secure_origins(string origin, bool development, bool valid)
    {
        var config = Config().Value;
        config.AllowedOrigins = [origin];
        Assert.Equal(valid, config.IsValid(development));
    }

    [Theory]
    [InlineData("", "test-client-secret")]
    [InlineData("123-test.apps.googleusercontent.com", "")]
    [InlineData("malformed.apps.googleusercontent.com", "secret")]
    [InlineData("https://evil.apps.googleusercontent.com", "secret")]
    public void Enabled_configuration_rejects_missing_or_malformed_client_credentials(string clientId, string secret)
    {
        var config = Config().Value; config.ClientId = clientId; config.ClientSecret = secret;
        Assert.False(config.IsValid(false));
    }

    private static IOptions<GoogleAuthOptions> Config() => Options.Create(new GoogleAuthOptions
    { Enabled = true, ClientId = ClientId, ClientSecret = "test-client-secret", AllowedOrigins = ["https://frontend.example.test"] });

    internal sealed class SignedTokenFixture : IDisposable
    {
        private readonly RSA rsa = RSA.Create(2048);
        public GoogleIdTokenValidator Validator { get; }
        public SignedTokenFixture()
        {
            var metadata = new OpenIdConnectConfiguration();
            metadata.SigningKeys.Add(new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = "test-only-signing-key" });
            Validator = new(new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata), Config(), TimeProvider.System);
        }
        public string Token(string? fault = null)
        {
            var now = DateTimeOffset.UtcNow;
            var payload = new JwtPayload
            {
                ["iss"] = fault == "issuer" ? "https://evil.test" : "https://accounts.google.com",
                ["aud"] = fault == "audience" ? "different-client.apps.googleusercontent.com" : ClientId,
                ["iat"] = now.ToUnixTimeSeconds() + (fault == "future-issued" ? 120 : 0),
                ["exp"] = now.ToUnixTimeSeconds() + (fault == "expired" ? -120 : 3600),
                ["sub"] = "google-subject-123", ["email"] = "person@external.test",
                ["email_verified"] = fault != "unverified-email", ["name"] = "Google Person"
            };
            if (fault == "missing-issued") payload.Remove("iat");
            if (fault == "missing-subject") payload.Remove("sub");
            if (fault == "missing-email") payload.Remove("email");
            if (fault == "invalid-email") payload["email"] = "not-an-email";
            if (fault == "authorized-party") payload["azp"] = "untrusted-client.apps.googleusercontent.com";
            using var otherRsa = RSA.Create(2048);
            var key = new RsaSecurityKey(fault == "different-signing-key" ? otherRsa : rsa) { KeyId = "test-only-signing-key" };
            var header = fault == "unsigned" ? new JwtHeader() : fault == "hmac"
                ? new JwtHeader(new SigningCredentials(new SymmetricSecurityKey(new byte[32]), SecurityAlgorithms.HmacSha256))
                : new JwtHeader(new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
            return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
        }
        public void Dispose() => rsa.Dispose();
    }

    internal sealed class TestOnlyTokenHandler(string responseBody, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? Uri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? ContentType { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri!.AbsoluteUri; Method = request.Method;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(status) { Content = new StringContent(responseBody) };
        }
    }
    private sealed class TestOnlyClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
