using System.Net;
using System.Net.Http.Headers;
using SkiaSharp;
using TravelTracker.Users.Tests.Support;

namespace TravelTracker.Users.Tests;

/** Exercises verified image uploads through the authenticated Users HTTP boundary. */
public sealed class UserPictureApiTests
{
    private const string Picture = "/api/v1/users/me/picture";
    private const string Owner = "profile-picture-owner";

    [Theory]
    [InlineData("image/png", SKEncodedImageFormat.Png)]
    [InlineData("image/jpeg", SKEncodedImageFormat.Jpeg)]
    public async Task ValidUploadReturnsPrivateReadReferenceAndStoredPng(string mediaType, SKEncodedImageFormat format)
    {
        using var host = new UsersApiFactory();
        using var caller = host.Browser(host.Token(subject: Owner, scope: "users.read users.write"));
        using var profile = await caller.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        using var missing = await caller.GetAsync(Picture);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var upload = await UploadAsync(caller, Image(format), mediaType);

        Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);
        Assert.Equal(Picture, upload.Headers.Location?.OriginalString);
        using var read = await caller.GetAsync(Picture);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("image/png", read.Content.Headers.ContentType?.MediaType);
        Assert.True(read.Headers.CacheControl?.NoStore);
        Assert.Equal("nosniff", Assert.Single(read.Headers.GetValues("X-Content-Type-Options")));
        using var decoded = SKBitmap.Decode(await read.Content.ReadAsByteArrayAsync());
        Assert.NotNull(decoded);
        Assert.Equal(16, decoded.Width);
        Assert.Equal(16, decoded.Height);

        using var replacement = await UploadAsync(caller, Image(SKEncodedImageFormat.Png, 24), "image/png");
        Assert.Equal(HttpStatusCode.NoContent, replacement.StatusCode);
        using var replaced = SKBitmap.Decode(await caller.GetByteArrayAsync(Picture));
        Assert.NotNull(replaced);
        Assert.Equal(24, replaced.Width);
    }

    [Theory]
    [InlineData("declared-unsupported", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("spoofed", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("wrong-format", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("empty", HttpStatusCode.BadRequest)]
    [InlineData("truncated", HttpStatusCode.BadRequest)]
    [InlineData("corrupt-pixels", HttpStatusCode.BadRequest)]
    [InlineData("oversized-body", HttpStatusCode.RequestEntityTooLarge)]
    [InlineData("oversized-dimensions", HttpStatusCode.RequestEntityTooLarge)]
    public async Task RejectedUploadLeavesSavedPictureUnchanged(string scenario, HttpStatusCode expected)
    {
        using var host = new UsersApiFactory();
        using var caller = host.Browser(host.Token(subject: Owner, scope: "users.read users.write"));
        using var profile = await caller.GetAsync("/api/v1/users/me");
        var png = Image(SKEncodedImageFormat.Png);
        using var firstUpload = await UploadAsync(caller, png, "image/png");
        Assert.Equal(HttpStatusCode.NoContent, firstUpload.StatusCode);
        var original = await caller.GetByteArrayAsync(Picture);

        var (input, mediaType) = scenario switch
        {
            "declared-unsupported" => (png, "image/gif"),
            "spoofed" => ("GIF89a"u8.ToArray(), "image/png"),
            "wrong-format" => (png, "image/jpeg"),
            "empty" => (Array.Empty<byte>(), "image/png"),
            "truncated" => (png[..^12], "image/png"),
            "corrupt-pixels" => (CorruptPngPixels(png), "image/png"),
            "oversized-body" => (new byte[1024 * 1024 + 1], "image/png"),
            "oversized-dimensions" => (Image(SKEncodedImageFormat.Png, 513), "image/png"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        using var upload = await UploadAsync(caller, input, mediaType);

        Assert.Equal(expected, upload.StatusCode);
        Assert.Equal(original, await caller.GetByteArrayAsync(Picture));
    }

    [Fact]
    public async Task OnlyTheOwnerCanReadAndAWriteScopeIsRequiredToReplace()
    {
        using var host = new UsersApiFactory();
        using var owner = host.Browser(host.Token(subject: Owner, scope: "users.read users.write"));
        using var other = host.Browser(host.Token(subject: "another-user", scope: "users.read users.write"));
        using var reader = host.Browser(host.Token(subject: Owner, scope: "users.read"));
        using var anonymous = host.Browser();
        using var profile = await owner.GetAsync("/api/v1/users/me");
        var image = Image(SKEncodedImageFormat.Png);
        using var upload = await UploadAsync(owner, image, "image/png");
        Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);

        using var otherRead = await other.GetAsync(Picture);
        using var readOnlyWrite = await UploadAsync(reader, image, "image/png");
        using var anonymousRead = await anonymous.GetAsync(Picture);
        Assert.Equal(HttpStatusCode.NotFound, otherRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, readOnlyWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);
    }

    [Fact]
    public async Task UploadWithoutContentLengthStillEnforcesTheByteLimit()
    {
        using var host = new UsersApiFactory();
        using var owner = host.Browser(host.Token(subject: Owner, scope: "users.read users.write"));
        using var profile = await owner.GetAsync("/api/v1/users/me");
        using var body = new UnmeasuredBody(new byte[1024 * 1024 + 1]);
        body.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        using var upload = await owner.PutAsync(Picture, body);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, upload.StatusCode);
        using var read = await owner.GetAsync(Picture);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact]
    public async Task BannedAccountCanReadButCannotReplaceAndDeactivationBlocksRead()
    {
        using var host = new UsersApiFactory();
        using var owner = host.Browser(host.Token(subject: Owner, scope: "users.read users.write"));
        using var profile = await owner.GetAsync("/api/v1/users/me");
        using var initial = await UploadAsync(owner, Image(SKEncodedImageFormat.Png), "image/png");
        Assert.Equal(HttpStatusCode.NoContent, initial.StatusCode);
        var saved = await owner.GetByteArrayAsync(Picture);

        host.Repository.SetStatus(host.Repository.Account!.PublicId, TravelTracker.Users.Domain.Models.UserStatus.Banned);
        using var blocked = await UploadAsync(owner, Image(SKEncodedImageFormat.Png, 24), "image/png");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal(saved, await owner.GetByteArrayAsync(Picture));

        host.Repository.Deactivate();
        using var denied = await owner.GetAsync(Picture);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task ValidPictureSurvivesRestartAndAnInvalidReplacement()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        byte[] saved;
        using (var host = new UsersApiFactory { ProfileDatabaseConnection = database.ConnectionString })
        using (var caller = host.Browser(host.Token(subject: Owner, scope: "users.read users.write")))
        {
            using var profile = await caller.GetAsync("/api/v1/users/me");
            using var upload = await UploadAsync(caller, Image(SKEncodedImageFormat.Png), "image/png");
            Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);
            saved = await caller.GetByteArrayAsync(Picture);
        }

        using var restarted = new UsersApiFactory { ProfileDatabaseConnection = database.ConnectionString };
        using var owner = restarted.Browser(restarted.Token(subject: Owner, scope: "users.read users.write"));
        using var invalid = await UploadAsync(owner, [], "image/png");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(saved, await owner.GetByteArrayAsync(Picture));
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient caller, byte[] bytes, string mediaType)
    {
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return await caller.PutAsync(Picture, content);
    }

    private static byte[] Image(SKEncodedImageFormat format, int width = 16)
    {
        using var bitmap = new SKBitmap(width, 16);
        bitmap.Erase(SKColors.Teal);
        using var encoded = bitmap.Encode(format, 100);
        return encoded.ToArray();
    }

    private static byte[] CorruptPngPixels(byte[] png)
    {
        var corrupt = png.ToArray();
        var dataMarker = corrupt.AsSpan().IndexOf("IDAT"u8);
        Assert.True(dataMarker >= 0);
        // Break the zlib stream while keeping the PNG container and dimensions intact.
        corrupt[dataMarker + 4] = 0;
        return corrupt;
    }

    /** Sends the test bytes without advertising the body size to the server. */
    private sealed class UnmeasuredBody(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes.AsMemory()).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
