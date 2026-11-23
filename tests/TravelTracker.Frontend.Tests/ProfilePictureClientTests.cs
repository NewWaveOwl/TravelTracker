using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Proves the raw own-picture HTTP boundary and bounded private response handling without a live Users API. */
public sealed class ProfilePictureClientTests
{
    const string Path = "/api/v1/users/me/picture";
    static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aL1cAAAAASUVORK5CYII=");
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-22T10:00:00Z"));
    readonly SwitchableAuthentication authentication;

    public ProfilePictureClientTests()
    {
        authentication = new(new SessionRegistry(clock));
        authentication.SignIn("picture-token", clock.GetUtcNow().AddMinutes(5));
    }

    UsersClient Client(StubHandler handler) => new(new ProtectedClient(authentication,
        new ServiceDirectory(new Dictionary<string, string> { ["Users"] = "https://users.test" }),
        new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock,
        NullLogger<ProtectedClient>.Instance, authentication.Sessions), new ConfigurationBuilder().Build());

    static HttpResponseMessage Picture(byte[]? bytes = null, string contentType = "image/png")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes ?? Png) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return response;
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    public async Task UploadUsesRawBytesAndMatchingMediaTypeWithoutAMultipartName(string contentType)
    {
        var bytes = contentType == "image/png" ? Png : new byte[] { 255, 216, 255, 217 };
        var handler = new StubHandler(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("https://users.test" + Path, request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer picture-token", request.Headers.Authorization!.ToString());
            Assert.Equal("image/png", Assert.Single(request.Headers.Accept).MediaType);
            Assert.Equal(contentType, request.Content!.Headers.ContentType!.MediaType);
            Assert.Null(request.Content.Headers.ContentDisposition);
            Assert.Equal(bytes, await request.Content.ReadAsByteArrayAsync(token));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        await Client(handler).ReplacePictureAsync(bytes, contentType);
    }

    [Fact]
    public async Task UploadLocationCannotRedirectTheSubsequentProtectedRead()
    {
        var requests = new List<string>();
        var client = Client(new StubHandler((request, _) =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            if (request.Method == HttpMethod.Get) return Task.FromResult(Picture());
            var response = new HttpResponseMessage(HttpStatusCode.NoContent);
            response.Headers.Location = new Uri("https://untrusted.test/picture?credential=never");
            return Task.FromResult(response);
        }));

        await client.ReplacePictureAsync(Png, "image/png");
        var actual = await client.GetPictureAsync();

        Assert.Equal(Png, actual);
        Assert.Equal(new[] { "https://users.test" + Path, "https://users.test" + Path }, requests);
    }

    [Theory]
    [InlineData(0, "image/png", 413)]
    [InlineData(1048577, "image/png", 413)]
    [InlineData(10, "image/gif", 415)]
    [InlineData(10, "image/svg+xml", 415)]
    public async Task InvalidUploadMetadataNeverSendsAnHttpRequest(int length, string type, int status)
    {
        var handler = StubHandler.Status(204);

        var failure = await Assert.ThrowsAsync<ApiError>(() => Client(handler).ReplacePictureAsync(new byte[length], type));

        Assert.Equal((FailureKind.Validation, status), (failure.Kind, failure.Status));
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task MaximumInputByteLengthIsAcceptedAtTheHttpBoundary()
    {
        var handler = StubHandler.Status(204);
        await Client(handler).ReplacePictureAsync(new byte[UsersClient.MaxPictureUploadBytes], "image/png");
        Assert.Equal(UsersClient.MaxPictureUploadBytes, handler.LastRequest!.Content!.Headers.ContentLength);
    }

    [Theory]
    [InlineData(400, FailureKind.Validation)]
    [InlineData(413, FailureKind.Validation)]
    [InlineData(415, FailureKind.Validation)]
    [InlineData(403, FailureKind.Permission)]
    [InlineData(409, FailureKind.Unexpected)]
    [InlineData(503, FailureKind.Unavailable)]
    public async Task UploadFailuresKeepTheServiceStatusAndSafeCategory(int status, FailureKind kind)
    {
        var failure = await Assert.ThrowsAsync<ApiError>(() => Client(StubHandler.Status(status)).ReplacePictureAsync(Png, "image/png"));
        Assert.Equal((kind, status), (failure.Kind, failure.Status));
    }

    [Fact]
    public async Task MissingPictureUsesTheInitialsFallbackContract()
    {
        Assert.Null(await Client(StubHandler.Status(404)).GetPictureAsync());
    }

    [Theory]
    [InlineData(1048577)]
    [InlineData(2097152)]
    public async Task NormalizedPngHasAnIndependentBoundAboveTheInputCap(int length)
    {
        var bytes = new byte[length];
        Png.CopyTo(bytes, 0);
        Assert.Equal(bytes, await Client(new StubHandler((_, _) => Task.FromResult(Picture(bytes)))).GetPictureAsync());
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("application/json")]
    public async Task ReadRejectsOtherMediaTypesInsteadOfEmbeddingThem(string mediaType)
    {
        var failure = await Assert.ThrowsAsync<ApiError>(() => Client(new StubHandler((_, _) => Task.FromResult(Picture(contentType: mediaType)))).GetPictureAsync());
        Assert.Equal(FailureKind.Unexpected, failure.Kind);
    }

    [Fact]
    public async Task StreamedPictureWithoutContentLengthStopsAtTheResponseCap()
    {
        using var stream = new EndlessStream();
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        Assert.Null(response.Content.Headers.ContentLength);

        await Assert.ThrowsAsync<ApiError>(() => Client(new StubHandler((_, _) => Task.FromResult(response))).GetPictureAsync());

        Assert.Equal(UsersClient.MaxPictureReadBytes + 1, stream.BytesRead);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionReplacementDiscardsLateReadAndWriteSuccesses(bool upload)
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = Client(new StubHandler((_, _) => response.Task));
        var pending = upload ? client.ReplacePictureAsync(Png, "image/png") : (Task)client.GetPictureAsync();
        authentication.SignInAndNotify("new-owner-token", clock.GetUtcNow().AddMinutes(5));
        response.SetResult(upload ? new HttpResponseMessage(HttpStatusCode.NoContent) : Picture());

        var failure = await Assert.ThrowsAsync<ApiError>(() => pending);

        Assert.Equal(401, failure.Status);
        Assert.True(authentication.Sessions.IsActive(authentication.User));
    }

    [Fact]
    public async Task FileSelectionBoundToAnOldSessionCannotUploadAsANewOwner()
    {
        var oldSession = SessionRegistry.IdFor(authentication.User);
        authentication.SignInAndNotify("new-owner-token", clock.GetUtcNow().AddMinutes(5));
        var handler = StubHandler.Status(204);

        var failure = await Assert.ThrowsAsync<ApiError>(() => Client(handler).ReplacePictureAsync(Png, "image/png", oldSession));

        Assert.Equal(401, failure.Status);
        Assert.Null(handler.LastRequest);
    }

    /** A non-seekable stream that proves a malicious body cannot force an unbounded image read. */
    sealed class EndlessStream : Stream
    {
        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { Array.Clear(buffer, offset, count); BytesRead += count; return count; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { buffer.Span.Clear(); BytesRead += buffer.Length; return ValueTask.FromResult(buffer.Length); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
