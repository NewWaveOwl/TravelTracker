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

    [Theory]
    [InlineData("pat@example.test", "pat")]
    [InlineData("@example.test", "Traveller")]
    public void DefaultDisplayNameIsTheEmailLocalPart(string email, string expected) =>
        Assert.Equal(expected, UserAccount.CreateTraveller("https://identity.example/", "s", email, Created).DisplayName);

    [Fact]
    public void DefaultDisplayNameIsCutToTheLimit() =>
        Assert.Equal(60, UserAccount.CreateTraveller("https://identity.example/", "s", new string('a', 90) + "@example.test", Created).DisplayName.Length);

    [Theory]
    [InlineData("Pat", "Pat", null, true)]
    [InlineData("  Pat  ", "Pat", "  ", true)]
    [InlineData("", "", null, false)]
    [InlineData("a\nb", "a\nb", null, false)]
    public void ProfileNormalizationTrimsAndValidates(string displayName, string expectedName, string? address, bool valid)
    {
        Assert.Equal(valid, UserAccount.TryNormalizeProfile(displayName, address, out var name, out var place, out var errors));
        Assert.Equal(valid, errors.Count == 0);
        if (valid) { Assert.Equal(expectedName.Trim(), name); Assert.Null(place); }
    }
}
