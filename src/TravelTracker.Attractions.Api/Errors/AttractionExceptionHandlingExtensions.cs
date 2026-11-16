namespace TravelTracker.Attractions.Api.Errors;

/**
 * Composes safe failure diagnostics, including aborts handled early by ASP.NET Core.
 */
public static class AttractionExceptionHandlingExtensions
{
    /**
     * Registers safe error responses and warning-level request-abort diagnostics.
     * @param app The owning request pipeline.
     * @return The pipeline with exception handling and abort logging installed.
     */
    public static IApplicationBuilder UseAttractionExceptionHandling(this IApplicationBuilder app)
    {
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandler = AttractionExceptionHandler.HandleAsync,
            // the handler emits safe diagnostics; suppress duplicate raw framework exception logs.
            SuppressDiagnosticsCallback = _ => true
        });
        return app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (Exception exception) when (context.RequestAborted.IsCancellationRequested &&
                exception is OperationCanceledException or IOException)
            {
                // aborted requests are skipped by the exception handler delegate, so log them here
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(AttractionExceptionHandler.LoggerCategory)
                    .LogWarning("Attraction request was canceled by the caller; status {StatusCode}.", 499);
                throw;
            }
        });
    }
}
