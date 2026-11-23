using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace TravelTracker.Trips.Api.TicketImports;

/** Prevents MVC's form value providers from reading or spooling a multipart body before the raw-PDF action. */
[AttributeUsage(AttributeTargets.Class)]
public sealed class RawPdfBodyAttribute : Attribute, IResourceFilter
{
    /**
     * Removes form readers before model binding; only explicit route and header metadata is bound.
     * @param context Current resource request.
     */
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        context.ValueProviderFactories.RemoveType<FormValueProviderFactory>();
        context.ValueProviderFactories.RemoveType<FormFileValueProviderFactory>();
        context.ValueProviderFactories.RemoveType<JQueryFormValueProviderFactory>();
    }

    /**
     * Leaves response processing to the action.
     * @param context Completed resource request.
     */
    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
