using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Components.Shared;

namespace TravelTracker.Frontend.Tests;

public sealed class CreateRetryWarningTests
{
    [Theory]
    [InlineData(FailureKind.Network)]
    [InlineData(FailureKind.Unavailable)]
    public void WarnsWhenACreateMayHaveReachedTheService(FailureKind kind) => Assert.True(CreateRetryWarning.Applies(new ApiError(kind, "failed")));

    [Fact]
    public void WarnsWhenASuccessfulCreateCannotBeConfirmed() => Assert.True(CreateRetryWarning.Applies(new ApiError(FailureKind.Unexpected, "The save could not be confirmed.")));

    [Theory]
    [InlineData(FailureKind.Validation, 422)]
    [InlineData(FailureKind.Permission, 403)]
    [InlineData(FailureKind.Unexpected, 404)]
    public void StaysQuietWhenTheServiceClearlyRejectedTheCreate(FailureKind kind, int status) => Assert.False(CreateRetryWarning.Applies(new ApiError(kind, "rejected", status)));
}
