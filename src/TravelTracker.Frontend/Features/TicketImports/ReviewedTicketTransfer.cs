namespace TravelTracker.Frontend.Features.TicketImports;

/** Carries a single reviewed ticket between pages in the current interactive circuit. */
public sealed class ReviewedTicketTransfer
{
    (Guid Trip, string Session, TicketReviewDraft Draft)? pending;

    public void Set(Guid trip, string session, TicketReviewDraft draft) => pending = (trip, session, draft);

    internal TicketReviewDraft? Take(Guid trip, string? session)
    {
        var previous = pending;
        pending = null;
        return previous is { } ticket && ticket.Trip == trip && ticket.Session == session ? ticket.Draft : null;
    }
}
