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

    [Fact]
    public async Task ExifOrientationSixRotatesTheSavedPixelsClockwise()
    {
        using var host = new UsersApiFactory();
        using var caller = host.Browser(host.Token(subject: Owner, scope: "users.read users.write"));
        using var profile = await caller.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var jpeg = CreateOrientationSixJpeg();
        using var upload = await UploadAsync(caller, jpeg, "image/jpeg");
        Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);

        using var saved = SKBitmap.Decode(await caller.GetByteArrayAsync(Picture));
        Assert.NotNull(saved);
        Assert.Equal(16, saved.Width);
        Assert.Equal(32, saved.Height);
        var top = saved.GetPixel(8, 4);
        var bottom = saved.GetPixel(8, 27);
        // Sample away from the boundary and allow for JPEG compression.
        Assert.True(top.Red > 200 && top.Blue < 50);
        Assert.True(bottom.Blue > 200 && bottom.Red < 50);
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
            "declared-unsupported" => (Image(SKEncodedImageFormat.Png, 24), "image/gif"),
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

    /**
     * Creates a two-color JPEG with a little-endian EXIF orientation tag set to 6.
     * @return JPEG bytes with the APP1 segment inserted after the start marker.
     */
    private static byte[] CreateOrientationSixJpeg()
    {
        using var bitmap = new SKBitmap(32, 16);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.Red })
        {
            canvas.Clear(SKColors.Blue);
            canvas.DrawRect(0, 0, 16, 16, paint);
        }
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 100);
        var jpeg = encoded.ToArray();
        byte[] exif =
        [
            0xff, 0xe1, 0x00, 0x22,
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            0x49, 0x49, 42, 0, 8, 0, 0, 0, // TIFF header and first directory offset
            1, 0,                            // one directory entry
            0x12, 0x01, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, // Orientation, SHORT, one value
            0, 0, 0, 0                      // no next directory
        ];
        return [.. jpeg.AsSpan(0, 2), .. exif, .. jpeg.AsSpan(2)];
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
