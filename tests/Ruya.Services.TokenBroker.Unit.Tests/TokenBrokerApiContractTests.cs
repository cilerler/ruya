using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Ruya.Services.DistributedLock.Abstractions;
using Ruya.Services.TokenBroker.Contracts;
using Ruya.Services.TokenBroker.Models;

namespace Ruya.Services.TokenBroker.Unit.Tests;

[TestClass]
public class TokenBrokerApiContractTests
{
    [TestMethod]
    public async Task MapTokenBrokerApi_MissingCredentials_ReturnsUnauthorized()
    {
        await using var fixture = await ApiFixture.CreateAsync();

        using var response = await fixture.PostAsync("/api/v1/token", "{}", authenticate: false);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(0, fixture.Broker.CreateCalls);
    }

    [TestMethod]
    public async Task MapTokenBrokerApi_VersionedAndLegacyCreate_SecuresBothAndDeprecatesLegacy()
    {
        await using var fixture = await ApiFixture.CreateAsync();

        using var versioned = await fixture.PostAsync("/api/v1/token", "{}");
        using var legacy = await fixture.PostAsync("/api/token", "{}");

        Assert.AreEqual(HttpStatusCode.OK, versioned.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, legacy.StatusCode);
        Assert.IsTrue(legacy.Headers.TryGetValues("Deprecation", out var values));
        CollectionAssert.Contains(values.ToArray(), "true");
        Assert.AreEqual(2, fixture.Broker.CreateCalls);
    }

    [TestMethod]
    public async Task MapTokenBrokerApi_ServiceNameDoesNotMatchRegistration_ReturnsUnauthorized()
    {
        await using var fixture = await ApiFixture.CreateAsync();

        using var response = await fixture.PostAsync(
            "/api/v1/token",
            "{}",
            serviceName: "different-service");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(0, fixture.Broker.CreateCalls);
    }

    [TestMethod]
    public async Task MapTokenBrokerApi_RoleOutsideRegistration_ReturnsBadRequestWithoutIssuing()
    {
        await using var fixture = await ApiFixture.CreateAsync();

        using var response = await fixture.PostAsync(
            "/api/v1/token",
            "{\"roles\":[\"administrator\"]}");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, fixture.Broker.CreateCalls);
    }

    [TestMethod]
    public async Task MapTokenBrokerApi_AllowedRoleAndScope_PassesRegistrationBoundsToIssuer()
    {
        await using var fixture = await ApiFixture.CreateAsync();

        using var response = await fixture.PostAsync(
            "/api/v1/token",
            "{\"roles\":[\"reader\"],\"scopes\":[\"read\"]}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(fixture.Broker.LastCreateRequest);
        CollectionAssert.Contains(fixture.Broker.LastCreateRequest.AllowedRoles!.ToArray(), "reader");
        CollectionAssert.Contains(fixture.Broker.LastCreateRequest.Scopes!.ToArray(), "read");
    }

    [TestMethod]
    public async Task MapTokenBrokerApi_ValidateEndpoint_RequiresCredentials()
    {
        await using var fixture = await ApiFixture.CreateAsync();

        using var unauthorized = await fixture.PostAsync(
            "/api/v1/token/validate",
            "{\"token\":\"valid-token\"}",
            authenticate: false);
        using var authorized = await fixture.PostAsync(
            "/api/v1/token/validate",
            "{\"token\":\"valid-token\"}");

        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, authorized.StatusCode);
        Assert.AreEqual(1, fixture.Broker.ValidateCalls);
    }

    [TestMethod]
    public async Task MapTokenBrokerApi_ExchangeNotAllowed_ReturnsForbiddenWithoutExchanging()
    {
        await using var fixture = await ApiFixture.CreateAsync();
        fixture.Validator.Registration = fixture.Validator.Registration with { CanExchangeTokens = false };

        using var response = await fixture.PostAsync(
            "/api/v1/token/exchange",
            "{\"token\":\"valid-token\",\"narrowedScopes\":[\"read\"]}");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.AreEqual(0, fixture.Broker.ExchangeCalls);
    }

    private sealed class ApiFixture : IAsyncDisposable
    {
        private const string ApiKey = "valid-api-key-1234567890";
        private readonly WebApplication _application;

        private ApiFixture(
            WebApplication application,
            HttpClient client,
            FakeTokenBroker broker,
            FakeApiKeyValidator validator)
        {
            _application = application;
            Client = client;
            Broker = broker;
            Validator = validator;
        }

        public HttpClient Client { get; }
        public FakeTokenBroker Broker { get; }
        public FakeApiKeyValidator Validator { get; }

        public static async Task<ApiFixture> CreateAsync()
        {
            var broker = new FakeTokenBroker();
            var validator = new FakeApiKeyValidator
            {
                Registration = new ServiceRegistration
                {
                    ServiceName = "service-a",
                    ApiKeyHash = string.Empty,
                    AllowedScopes = ["read"],
                    AllowedRoles = ["reader"],
                    CanExchangeTokens = true
                }
            };
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSingleton<IDistributedLock>(new PassThroughDistributedLock());
            builder.Services.AddSingleton<ITokenBroker>(broker);
            builder.Services.AddSingleton<IApiKeyValidator>(validator);
            builder.Services.AddTokenBroker(settings =>
            {
                settings.Issuer = "test-issuer";
                settings.Audiences.Add("test-audience");
                settings.SigningKeyId = TestSigningKeys.KeyId;
                settings.SigningPrivateKeyPem = TestSigningKeys.PrivateKeyPem;
                settings.SigningPublicKeys.Add(TestSigningKeys.KeyId, TestSigningKeys.PublicKeyPem);
            });

            var application = builder.Build();
            application.MapTokenBrokerApi();
            await application.StartAsync();
            var client = new HttpClient
            {
                BaseAddress = new Uri(application.Urls.Single())
            };
            return new ApiFixture(application, client, broker, validator);
        }

        public async Task<HttpResponseMessage> PostAsync(
            string path,
            string json,
            bool authenticate = true,
            string serviceName = "service-a")
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (authenticate)
            {
                request.Headers.Add(Constants.ApiKeyHeader, ApiKey);
                request.Headers.Add(Constants.ServiceNameHeader, serviceName);
            }

            return await Client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _application.DisposeAsync();
        }

        public sealed class FakeTokenBroker : ITokenBroker
        {
            public int CreateCalls { get; private set; }
            public int ExchangeCalls { get; private set; }
            public int ValidateCalls { get; private set; }
            public TokenRequest? LastCreateRequest { get; private set; }

            public Task<TokenResponse> CreateTokenAsync(
                TokenRequest request,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CreateCalls++;
                LastCreateRequest = request;
                return Task.FromResult(CreateResponse(request.Subject, request.Scopes));
            }

            public Task<TokenResponse> ExchangeTokenAsync(
                TokenExchangeRequest request,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ExchangeCalls++;
                return Task.FromResult(CreateResponse("original-service", request.NarrowedScopes));
            }

            public Task<TokenValidationResult> ValidateTokenAsync(
                string token,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateCalls++;
                return Task.FromResult(new TokenValidationResult
                {
                    IsValid = string.Equals(token, "valid-token", StringComparison.Ordinal),
                    Subject = "original-service"
                });
            }

            private static TokenResponse CreateResponse(
                string subject,
                System.Collections.Generic.IReadOnlyList<string>? scopes) => new()
                {
                    AccessToken = "generated-token",
                    TokenType = "Bearer",
                    ExpiresIn = 900,
                    ExpiresAt = new DateTime(2030, 1, 1, 0, 15, 0, DateTimeKind.Utc),
                    Subject = subject,
                    Scopes = scopes
                };
        }

        public sealed class FakeApiKeyValidator : IApiKeyValidator
        {
            public required ServiceRegistration Registration { get; set; }

            public Task<ServiceRegistration?> ValidateApiKeyAsync(
                string apiKey,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult<ServiceRegistration?>(
                    string.Equals(apiKey, ApiKey, StringComparison.Ordinal) ? Registration : null);
            }

            public Task RegisterServiceAsync(
                ServiceRegistration registration,
                string apiKey,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }

            public Task RemoveServiceAsync(
                string serviceName,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        }
    }
}
