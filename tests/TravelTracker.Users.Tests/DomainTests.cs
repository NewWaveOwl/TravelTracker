using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Tests;

public sealed class DomainTests
{
    static readonly DateTimeOffset Created = DateTimeOffset.Parse("2026-11-04T16:00:00Z");
    static readonly ProviderIdentity Identity = new("https://identity.example/", "subject-1");

    [Fact]
    public void OrdinaryProvisioningCreatesAnActiveTravellerWithANewPublicId()
    {
        var account = UserAccount.CreateTraveller(Identity, Created);
        Assert.NotEqual(Guid.Empty, account.PublicId);
        Assert.Equal(7, account.PublicId.Version);
        Assert.Equal(UserRole.User, account.Role);
        Assert.Equal(UserStatus.Active, account.Status);
        Assert.Equal(Created, account.CreatedAt);
        Assert.Equal(Identity, account.ProviderIdentity);
        Assert.Null(account.Email);
    }

    [Fact]
    public void AuthorizedBootstrapCreatesAnAdministratorWithoutChangingProviderIdentity()
    {
        var account = UserAccount.CreateAdministrator(Identity, Created);
        Assert.Equal(UserRole.Administrator, account.Role);
        Assert.Equal(UserStatus.Active, account.Status);
        Assert.Equal(Identity, account.ProviderIdentity);
    }

    [Theory]
    [InlineData("", "subject-1")]
    [InlineData("  ", "subject-1")]
    [InlineData("https://identity.example/", "")]
    [InlineData("https://identity.example/", "  ")]
    public void ProviderIdentityRejectsMissingComponents(string issuer, string subject) =>
        Assert.Throws<ArgumentException>(() => new ProviderIdentity(issuer, subject));

    [Fact]
    public void UnsupportedRolesOrStandingAreRejected()
    {
        var account = UserAccount.CreateTraveller(Identity, Created) with { Role = (UserRole)99, Status = (UserStatus)99 };
        Assert.Contains("role", account.Validate());
        Assert.Contains("status", account.Validate());
    }

    [Fact]
    public void RestoredAccountsRequireAPublicIdentifier()
    {
        var account = UserAccount.CreateAdministrator(Identity, Created) with { PublicId = Guid.Empty };
        Assert.Contains("publicId", account.Validate());
    }
}
