using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.TicketImports;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Features.TicketImports;

namespace TravelTracker.Frontend.Components.Pages;

/** Coordinates an owner-scoped temporary PDF review without writing to the saved itinerary. */
public partial class TicketImport : IAsyncDisposable
{
    [Parameter] public Guid TripId { get; set; }
    [Inject] TripsClient Trips { get; set; } = default!;
    [Inject] TicketImportsClient Imports { get; set; } = default!;
    [Inject] UsersClient Users { get; set; } = default!;
    [Inject] AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] TimeProvider Clock { get; set; } = default!;

    [Inject] NavigationManager Navigation { get; set; } = default!;
    [Inject] ReviewedTicketTransfer Transfer { get; set; } = default!;
    string pastedText = "";
    string? textError;
    bool readingText;

    enum ImportStage { Upload, Extracting, Review, Reviewed, Failed }
    ImportStage stage;
    TripDto? trip;
    TicketImportDto? source;
    TicketReviewDraft? draft;
    ApiError? failure;
    CancellationTokenSource? request;
    ITimer? expiry;
    bool loading = true, canWrite, validationFailed, disposed, sourceExpired;
    string fileName = "", notice = "", readOnlyReason = "Importing tickets requires an active account and the trips.write permission.";
    long fileSize;
    Guid loadedId;
    int generation;
    string TripLink => $"trips/{TripId:D}";
    string DocumentLink => $"ticket-documents/{TripId:D}/{source!.ImportId:D}";
    string FileSize => fileSize >= 1024 * 1024 ? $"{fileSize / (1024d * 1024):0.#} MB" : $"{Math.Max(1, fileSize / 1024)} KB";
    bool InputFailure => failure?.Status is 400 or 413 or 415 or 422;
    string Title => stage switch
    {
        ImportStage.Extracting => "Reading your ticket", ImportStage.Review => "Review ticket details",
        ImportStage.Reviewed => "Ticket draft reviewed", ImportStage.Failed => "We couldn’t read this PDF", _ => "Import tickets"
    };
    string Introduction => stage switch
    {
        ImportStage.Extracting => "Keep this page open while your ticket is processed.",
        ImportStage.Review => $"Check the extracted details for {trip?.Name} before using them.",
        ImportStage.Reviewed => "Your confirmed details are shown below. They have not been saved.",
        ImportStage.Failed => "No ticket has been added. Your existing trip is unchanged.",
        _ => $"Review a ticket or booking confirmation for {trip?.Name}."
    };

    /**
     * Loads only an accessible saved trip when its route changes.
     * @return Completion of the owner-scoped read.
     */
    protected override async Task OnParametersSetAsync()
    {
        if (loadedId == TripId) return;
        loadedId = TripId;
        Authentication.AuthenticationStateChanged -= AuthenticationChanged;
        Authentication.AuthenticationStateChanged += AuthenticationChanged;
        await LoadAsync();
    }

    async Task LoadAsync()
    {
        request?.Cancel();
        request?.Dispose();
        var current = request = new CancellationTokenSource();
        var version = ++generation;
        ClearLocal();
        loading = true;
        try
        {
            var user = (await Authentication.GetAuthenticationStateAsync()).User;
            var scopes = user.FindAll(AccessTokenClaims.GrantedScope)
                .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToHashSet(StringComparer.Ordinal);
            var hasWriteScope = user.Identity?.IsAuthenticated == true && scopes.Contains("trips.write");
            canWrite = false;
            var loaded = await Trips.GetAsync(TripId, current.Token);
            if (disposed || generation != version) return;
            trip = loaded;
            if (!hasWriteScope) readOnlyReason = "This session does not include trips.write. You can view your trip but cannot import tickets.";
            else if (!scopes.Contains("users.read")) readOnlyReason = "Your account status cannot be verified in this session. Sign in with users.read to enable ticket import.";
            else
            {
                try
                {
                    var profile = await Users.GetMeAsync(current.Token);
                    if (disposed || generation != version) return;
                    canWrite = profile is { Status: "active" };
                    readOnlyReason = profile?.Status == "banned" ? "Your account is read-only. You can view your trip but cannot import or edit tickets."
                        : "Your active account status could not be confirmed. Reopen the trip after your account is available.";
                }
                catch (ApiError)
                { readOnlyReason = "Your account status could not be verified. Ticket import is read-only until the Users service is available."; }
            }
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { return; }
        catch (ApiError error) { if (!disposed && generation == version) failure = error; }
        finally { if (!disposed && generation == version) loading = false; }
    }

    void AuthenticationChanged(Task<AuthenticationState> state) => _ = InvokeAsync(async () =>
    {
        if (disposed) return;
        var load = LoadAsync();
        StateHasChanged();
        await load;
        if (!disposed) StateHasChanged();
    });

    /**
     * Streams one selected PDF using the declared maximum rather than trusting browser metadata.
     * @param args File picker selection.
     * @return Completion of the extraction request, or a recoverable failed state.
     */
    async Task ChoosePdfAsync(InputFileChangeEventArgs args)
    {
        if (stage == ImportStage.Extracting || !canWrite) return;
        if (args.FileCount != 1) return;
        var file = args.File;
        fileName = file.Name;
        fileSize = file.Size;
        notice = "";
        failure = null;
        if (file.Size <= 0 || file.Size > TicketImportsClient.MaximumBytes)
        {
            failure = new ApiError(FailureKind.Validation, "Choose a non-empty PDF up to 10 MB and 10 pages.", 413);
            stage = ImportStage.Failed;
            return;
        }
        if (!file.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            failure = new ApiError(FailureKind.Validation, "Choose a PDF document. Other file types are not supported.", 415);
            stage = ImportStage.Failed;
            return;
        }
        request?.Cancel();
        request?.Dispose();
        var current = request = new CancellationTokenSource();
        var version = ++generation;
        stage = ImportStage.Extracting;
        try
        {
            await using var stream = file.OpenReadStream(TicketImportsClient.MaximumBytes, current.Token);
            var extracted = await Imports.UploadAsync(TripId, stream, fileName, current.Token);
            if (disposed || generation != version) return;
            source = extracted;
            draft = TicketReviewDraft.From(source);
            stage = ImportStage.Review;
            sourceExpired = source.ExpiresAt <= Clock.GetUtcNow();
            expiry?.Dispose();
            if (!sourceExpired) expiry = Clock.CreateTimer(_ => _ = InvokeAsync(() =>
            {
                if (disposed) return;
                sourceExpired = true;
                StateHasChanged();
            }), null, source.ExpiresAt - Clock.GetUtcNow(), Timeout.InfiniteTimeSpan);
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        catch (ApiError error)
        {
            if (disposed || generation != version) return;
            failure = error;
            stage = ImportStage.Failed;
            if (error.Status == 403) { canWrite = false; readOnlyReason = error.Message; }
        }
        catch (IOException)
        {
            if (disposed || generation != version) return;
            failure = new ApiError(FailureKind.Validation, "The selected PDF could not be read. Choose it again or enter the details manually.", 400);
            stage = ImportStage.Failed;
        }
    }

    async Task ParseTextAsync()
    {
        if (!canWrite || stage != ImportStage.Upload) return;
        textError = null;
        var text = pastedText.ReplaceLineEndings("\n");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000 || text.Split('\n').Length > 60 ||
            text.Contains('\0') || System.Text.Encoding.UTF8.GetByteCount(text) > 12000)
        {
            textError = "Enter ticket text within the displayed limits.";
            return;
        }
        var version = ++generation;
        var token = request!.Token;
        stage = ImportStage.Extracting;
        readingText = true;
        try
        {
            var fields = await Imports.ParseTextAsync(TripId, text, token);
            if (disposed || generation != version) return;
            draft = TicketReviewDraft.FromFields(fields);
            pastedText = "";
            stage = ImportStage.Review;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ApiError error) when (!disposed && generation == version)
        {
            textError = "The ticket text could not be read. Check the text and try again, or enter the details manually.";
            if (error.Status == 403) canWrite = false;
            stage = ImportStage.Upload;
        }
        catch (ApiError) when (disposed || generation != version) { }
        finally { if (generation == version) readingText = false; }
    }

    async Task OpenEditorAsync()
    {
        var version = generation;
        var session = SessionRegistry.IdFor((await Authentication.GetAuthenticationStateAsync()).User);
        if (session is null || version != generation || disposed || !canWrite || stage != ImportStage.Reviewed || draft is null) return;
        Transfer.Set(TripId, session, draft);
        Navigation.NavigateTo($"trip/{TripId:D}");
    }

    void FinishReview()
    {
        if (draft is null) return;
        validationFailed = !draft.Validate();
        if (!validationFailed) stage = ImportStage.Reviewed;
    }

    void EditAgain() => stage = ImportStage.Review;

    void EnterManually()
    {
        failure = null;
        notice = "Manual entry. No source document is attached, and your saved trip is unchanged.";
        draft = TicketReviewDraft.From(null);
        stage = ImportStage.Review;
    }

    async Task ChooseAnotherAsync()
    {
        await CancelAsync();
        notice = "";
    }

    async Task CancelAsync()
    {
        request?.Cancel();
        ++generation;
        var previous = source;
        ClearLocal(keepTrip: true);
        notice = "Import cancelled. Your trip is unchanged.";
        if (previous is not null)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await Imports.CancelAsync(previous.TripId, previous.ImportId, cleanup.Token); }
            catch (Exception error) when (error is ApiError or OperationCanceledException)
            { notice = "Review cancelled. The private temporary PDF will expire automatically. Your trip is unchanged."; }
        }
    }

    void ClearLocal(bool keepTrip = false)
    {
        expiry?.Dispose();
        expiry = null;
        source = null;
        draft = null;
        sourceExpired = validationFailed = false;
        readingText = false;
        textError = null;
        pastedText = fileName = notice = "";
        failure = null;
        stage = ImportStage.Upload;
        if (!keepTrip) trip = null;
    }

    /**
     * Cancels in-flight work and releases any known temporary PDF when the review closes.
     * @return Completion of best-effort private document cleanup.
     */
    public async ValueTask DisposeAsync()
    {
        disposed = true;
        Authentication.AuthenticationStateChanged -= AuthenticationChanged;
        request?.Cancel();
        request?.Dispose();
        expiry?.Dispose();
        if (source is { } previous)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await Imports.CancelAsync(previous.TripId, previous.ImportId, cleanup.Token); }
            catch (Exception error) when (error is ApiError or OperationCanceledException) { }
        }
        ClearLocal();
    }
}
