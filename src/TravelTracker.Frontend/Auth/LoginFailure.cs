namespace TravelTracker.Frontend.Auth;

/** Maps provider failures to fixed codes and safe messages so raw diagnostics are never rendered. */
public static class LoginFailure
{
    public const string NotAuthorized = "not-authorized";
    public const string NotConfigured = "not-configured";
    public const string LoginRequired = "login-required";
    public const string ConsentRequired = "consent-required";
    public const string Canceled = "canceled";
    public const string StartFailed = "start-failed";

    /**
     * Picks the failure code for a callback error.
     *
     * @param error The OAuth error code from the callback, potentially sensitive.
     * @param description The provider description from the callback, potentially sensitive.
     * @return One of the fixed codes; unknown failures map to Canceled.
     */
    public static string CodeFor(string? error, string? description)
    {
        if (description?.Contains("is not authorized to access resource server", StringComparison.Ordinal) == true)
            return NotAuthorized;
        if (description?.Contains("Service not found", StringComparison.Ordinal) == true)
            return NotConfigured;
        return error switch
        {
            "login_required" => LoginRequired,
            "consent_required" => ConsentRequired,
            _ => Canceled,
        };
    }

    /**
     * Describes a failure code for the traveller.
     *
     * @param code A code from CodeFor; anything else is treated as Canceled.
     * @return Static recovery text safe to render in the sign-in feedback panel.
     */
    public static string MessageFor(string? code) => code switch
    {
        NotAuthorized => "Sign-in is not available for this application yet. Ask the administrator to authorize its access, then try again.",
        NotConfigured => "The sign-in service is not fully configured. Ask the administrator to check the API settings, then try again.",
        LoginRequired => "Your sign-in session has ended. Sign in again to continue.",
        StartFailed => "Unable to start sign-in. Please try again.",
        ConsentRequired => "Permission is needed to continue. Sign in again and review the access request.",
        _ => "Sign-in was canceled or could not be completed. You can try again.",
    };
}
