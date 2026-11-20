using System.Text.Json.Serialization;

namespace TravelTracker.Users.Api.Contracts;

/** Whitelists editable profile fields (the username is fixed at registration) and distinguishes omitted values from explicit null. */
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateUserProfileRequest
{
    public string? Address
    {
        get;
        init
        {
            field = value;
            HasAddress = true;
        }
    }

    internal bool HasAddress { get; private set; }
}
