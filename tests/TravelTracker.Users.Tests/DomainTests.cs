using TravelTracker.Users.Domain.Models;

namespace TravelTracker.Users.Tests;

public sealed class DomainTests
{
    static readonly DateTimeOffset Created = DateTimeOffset.Parse("2026-11-04T16:00:00Z");

    [Fact]
    public void OrdinaryProvisioningCreatesAnActiveTravellerWithANewPublicId()
    {
        var account = UserAccount.CreateTraveller("https://identity.example/", "subject-1", "traveller@example.test", Created);
        Assert.NotEqual(Guid.Empty, account.UserId);
        Assert.Equal(7, account.UserId.Version);
        Assert.Equal("traveller", account.Role);
        Assert.Equal("active", account.Status);
        Assert.Equal(Created, account.CreatedAt);
        Assert.Equal("https://identity.example/", account.Issuer);
        Assert.Equal("subject-1", account.Subject);
    }

    [Fact]
    public void AuthorizedBootstrapCreatesAnAdministratorWithoutChangingProviderIdentity()
    {
        var account = UserAccount.CreateAdministrator("https://identity.example/", "subject-1", "admin@example.test", Created);
        Assert.Equal("administrator", account.Role);
        Assert.Equal("active", account.Status);
        Assert.Equal("subject-1", account.Subject);
        Assert.Equal("admin@example.test", account.Email);
    }

    [Theory]
    [InlineData("", "subject-1", "admin@example.test")]
    [InlineData("  ", "subject-1", "admin@example.test")]
    [InlineData("https://identity.example/", "", "admin@example.test")]
    [InlineData("https://identity.example/", "  ", "admin@example.test")]
    [InlineData("https://identity.example/", "subject-1", "")]
    [InlineData("https://identity.example/", "subject-1", "  ")]
    public void ProvisioningRejectsMissingIdentityAndEmail(string issuer, string subject, string email) =>
        Assert.Throws<ArgumentException>(() => UserAccount.CreateAdministrator(issuer, subject, email, Created));

    [Theory]
    [InlineData("admin", "active")]
    [InlineData("Administrator", "active")]
    [InlineData("administrator", "enabled")]
    [InlineData("traveller", "Active")]
    public void UnsupportedRolesOrStandingAreRejected(string role, string status) =>
        Assert.Throws<ArgumentException>(() => new UserAccount(Guid.CreateVersion7(), "https://identity.example/", "subject-1", "admin@example.test", role, status, Created));

    [Fact]
    public void RestoredAccountsRequireAPublicIdentifier() =>
        Assert.Throws<ArgumentException>(() => new UserAccount(Guid.Empty, "https://identity.example/", "subject-1", "admin@example.test", "administrator", "active", Created));
}
