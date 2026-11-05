using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Domain.Tests;

public sealed class UserAccountTests
{
    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.Parse("2026-11-04T16:00:00Z");
    private static readonly ProviderIdentity Identity = new("https://identity.example/", "subject-1");

    [Fact]
    public void CreateTravellerUsesUuidV7AndServerAssignedDefaults()
    {
        var account = UserAccount.CreateTraveller(Identity, CreatedAt);

        Assert.NotEqual(Guid.Empty, account.PublicId);
        Assert.Equal(7, account.PublicId.Version);
        Assert.Equal(UserRole.User, account.Role);
        Assert.Equal(UserStatus.Active, account.Status);
        Assert.Equal(Identity, account.ProviderIdentity);
        Assert.Null(account.Username);
        Assert.Null(account.Email);
        Assert.Null(account.Address);
        Assert.Equal(CreatedAt, account.CreatedAt);
        Assert.Null(account.UpdatedAt);
        Assert.Empty(account.Validate());
    }

    [Fact]
    public void CreateAdministratorUsesAdministratorRoleAndActiveStanding()
    {
        var account = UserAccount.CreateAdministrator(Identity, CreatedAt);

        Assert.Equal(UserRole.Administrator, account.Role);
        Assert.Equal(UserStatus.Active, account.Status);
        Assert.Equal(Identity, account.ProviderIdentity);
    }

    [Theory]
    [InlineData("", "subject")]
    [InlineData(" ", "subject")]
    [InlineData("issuer", "")]
    [InlineData("issuer", "  ")]
    public void ProviderIdentityRejectsMissingIssuerOrSubject(string issuer, string subject)
    {
        Assert.Throws<ArgumentException>(() => new ProviderIdentity(issuer, subject));
    }

    [Fact]
    public void ProviderIdentityDefaultValueIsNotValid()
    {
        Assert.False(default(ProviderIdentity).IsValid);
    }

    [Fact]
    public void UserRoleAndStatusKeepTheirStableNumericValues()
    {
        Assert.Equal(0, (int)UserRole.User);
        Assert.Equal(1, (int)UserRole.Administrator);
        Assert.Equal(0, (int)UserStatus.Active);
        Assert.Equal(1, (int)UserStatus.Deactivated);
        Assert.Equal(2, (int)UserStatus.Banned);
    }

    [Fact]
    public void ValidateAcceptsNullProfileFieldsAndAValidCompleteProfile()
    {
        var incomplete = UserAccount.CreateTraveller(Identity, CreatedAt);
        var complete = CreateAccount(username: "traveller_1", email: "traveller@example.test", address: "123 Main Street");

        Assert.Empty(incomplete.Validate());
        Assert.Empty(complete.Validate());
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData(" auser")]
    [InlineData("user ")]
    [InlineData("traveller!")]
    [InlineData("éuser")]
    public void ValidateRejectsInvalidUsernames(string username)
    {
        var errors = CreateAccount(username: username).Validate();

        Assert.Contains("username", errors.Keys);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("traveller.name_1-2")]
    [InlineData("12345678901234567890123456789012")]
    public void ValidateAcceptsAllowedUsernameValues(string username)
    {
        var errors = CreateAccount(username: username).Validate();

        Assert.DoesNotContain("username", errors.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-an-email")]
    [InlineData(" traveller@example.test")]
    public void ValidateRejectsInvalidEmailValues(string email)
    {
        var errors = CreateAccount(email: email).Validate();

        Assert.Contains("email", errors.Keys);
    }

    [Fact]
    public void ValidateRejectsEmailLongerThanContractLimit()
    {
        var email = $"{new string('a', 243)}@example.test";

        var errors = CreateAccount(email: email).Validate();

        Assert.Contains("email", errors.Keys);
    }

    [Fact]
    public void ValidateAcceptsEmailAtMaximumContractLength()
    {
        var email = $"{new string('a', 64)}@{new string('b', 63)}.{new string('c', 63)}.{new string('d', 61)}";

        Assert.Equal(254, email.Length);
        Assert.DoesNotContain("email", CreateAccount(email: email).Validate().Keys);
    }

    [Fact]
    public void ValidateAcceptsAddressAtMaximumLength()
    {
        var address = new string('a', 500);

        var errors = CreateAccount(address: address).Validate();

        Assert.DoesNotContain("address", errors.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" Address")]
    [InlineData("Address ")]
    public void ValidateRejectsInvalidAddressValues(string address)
    {
        var errors = CreateAccount(address: address).Validate();

        Assert.Contains("address", errors.Keys);
    }

    [Fact]
    public void ValidateRejectsAddressLongerThanContractLimit()
    {
        var address = new string('a', 501);

        var errors = CreateAccount(address: address).Validate();

        Assert.Contains("address", errors.Keys);
    }

    [Fact]
    public void ValidateReportsInvalidServerControlledValuesTogether()
    {
        var account = new UserAccount(
            Guid.Empty,
            default,
            (UserRole)99,
            (UserStatus)99,
            CreatedAt);

        var errors = account.Validate();

        Assert.Contains("publicId", errors.Keys);
        Assert.Contains("providerIdentity", errors.Keys);
        Assert.Contains("role", errors.Keys);
        Assert.Contains("status", errors.Keys);
    }

    private static UserAccount CreateAccount(
        string? username = null,
        string? email = null,
        string? address = null) =>
        new(Guid.CreateVersion7(), Identity, UserRole.User, UserStatus.Active, CreatedAt, username, email, address);
}
