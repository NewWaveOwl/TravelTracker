using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TravelTracker.Users.Api.Contracts;
using TravelTracker.Users.Tests.Support;

namespace TravelTracker.Users.Tests;

/** Exercises production profile persistence through the Users HTTP routes. */
public sealed class UserProfilePostgreSqlTests
{
    private const string Me = "/api/v1/users/me";
    private const string Subject = "concurrent-profile";

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ConcurrentPatchesReturnOneConflictAndOnlyTheWinnerSurvivesRestart()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var barrier = new ProfileSaveBarrier();
        SelfProfileResponse saved;
        using (var host = new UsersApiFactory
        {
            ProfileDatabaseConnection = database.ConnectionString,
            ProfileSaveInterceptor = barrier
        })
        using (var caller = host.Browser(host.Token(subject: Subject, scope: "users.read users.write")))
        {
            var provisioned = await caller.GetFromJsonAsync<SelfProfileResponse>(Me);
            barrier.Enabled = true;
            var responses = await Task.WhenAll(
                caller.PatchAsJsonAsync(Me, new { email = "edited@example.test" }),
                caller.PatchAsJsonAsync(Me, new { address = "1 Test Avenue" }));
            using var emailResponse = responses[0];
            using var addressResponse = responses[1];
            var updated = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var stale = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            var problem = await stale.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("profile_concurrency_conflict", problem.GetProperty("code").GetString());

            saved = (await updated.Content.ReadFromJsonAsync<SelfProfileResponse>())!;
            Assert.Equal(provisioned?.PublicId, saved.PublicId);
            Assert.Equal(emailResponse.IsSuccessStatusCode ? "edited@example.test" : null, saved.Email);
            Assert.Equal(addressResponse.IsSuccessStatusCode ? "1 Test Avenue" : null, saved.Address);
            Assert.NotNull(saved.UpdatedAt);
        }

        using var restartedHost = new UsersApiFactory { ProfileDatabaseConnection = database.ConnectionString };
        using var restartedCaller = restartedHost.Browser(restartedHost.Token(subject: Subject, scope: "users.read"));
        Assert.Equal(saved, await restartedCaller.GetFromJsonAsync<SelfProfileResponse>(Me));
    }

    private sealed class ProfileSaveBarrier : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource bothReachedSave = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public bool Enabled { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Enabled) return result;
            if (Interlocked.Increment(ref arrivals) == 2) bothReachedSave.SetResult();
            await bothReachedSave.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }
}
