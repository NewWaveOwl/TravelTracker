using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Tests.Support;

namespace TravelTracker.Users.Tests;

/** Registers, signs in and issues service tokens through the real controller with an in-memory credential store. */
public sealed class PasswordAuthTests : IDisposable
{
    readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    readonly UsersApiFactory factory = new();
    MemoryCredentialRepository store => factory.Credentials;

    HttpClient Client(bool keyConfigured = true) => factory.WithWebHostBuilder(builder =>
    {
        if (keyConfigured)
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authentication:Issuer:PrivateKey"] = key.ExportPkcs8PrivateKeyPem() }));
    }).CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    static async Task<JsonElement> Body(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>())!;

    [Fact]
    public async Task RegisterThenLogin_IssuesVerifiableTokenForTheSameAccount()
    {
        using var client = Client();
        var registered = await client.PostAsJsonAsync("/api/v1/auth/register", new { email = "Ada@Example.test", password = "correct horse" });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "ada@example.test", password = "correct horse" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync((await Body(login)).GetProperty("accessToken").GetString(),
            new TokenValidationParameters
            {
                ValidIssuer = UsersApiFactory.Issuer, ValidAudience = UsersApiFactory.Audience,
                IssuerSigningKey = new ECDsaSecurityKey(key), ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            });
        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(store.For("ada@example.test").Account.ProviderIdentity.Subject, result.Claims["sub"]);
        Assert.Contains("trips.write", result.Claims["scope"].ToString());
        Assert.DoesNotContain("correct horse", store.For("ada@example.test").PasswordHash);
    }

    [Theory]
    [InlineData("ada@example.test", "wrong password")]
    [InlineData("nobody@example.test", "correct horse")]
    public async Task Login_RefusesWrongPasswordAndUnknownLoginIdentically(string email, string password)
    {
        using var client = Client();
        await client.PostAsJsonAsync("/api/v1/auth/register", new { email = "ada@example.test", password = "correct horse" });
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_RefusesDuplicateAndWeakInput()
    {
        using var client = Client();
        var first = new { email = "ada@example.test", password = "correct horse" };
        await client.PostAsJsonAsync("/api/v1/auth/register", first);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/auth/register", first)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/register", new { email = "not-an-email", password = "correct horse" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/register", new { email = "b@example.test", password = "short" })).StatusCode);
    }

    [Fact]
    public async Task Login_RefusesAccountsThatAreNotActive()
    {
        using var client = Client();
        await client.PostAsJsonAsync("/api/v1/auth/register", new { email = "ada@example.test", password = "correct horse" });
        store.SetStatus("ada@example.test", UserStatus.Banned);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "ada@example.test", password = "correct horse" })).StatusCode);
    }

    [Fact]
    public async Task ServiceToken_NeedsTheOperatorCredentialAndGrantsOnlyUsersAccess()
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/service-token", null)).StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/service-token");
        request.Headers.Add("Authorization", "Bearer " + UsersApiFactory.ServiceToken);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("users.access", (await Body(response)).GetProperty("scope").GetString());
    }

    [Fact]
    public async Task WithoutASigningKey_AnswersUnavailableInsteadOfThrowing()
    {
        using var client = Client(keyConfigured: false);
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "ada@example.test", password = "correct horse" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    public void Dispose()
    {
        factory.Dispose();
        key.Dispose();
    }
}
