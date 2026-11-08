namespace TravelTracker.Frontend.Api.Users;

/**
 * The caller-editable profile fields, with changed fields serialized as a partial update.
 * @param username Requested username value.
 * @param email Requested contact email value.
 * @param address Requested address value; null clears the existing address.
 */
public sealed class UpdateSelfProfileRequest(string? username, string? email, string? address)
{
    /**
     * Builds the public PATCH body while preserving omitted fields and explicit address clearing.
     * @param current The latest server-confirmed own profile used as the comparison baseline.
     * @return Only changed fields; an empty username/email remains omitted when it was not set originally.
     */
    public IReadOnlyDictionary<string, string?> ChangedFields(SelfUserProfile current)
    {
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        var normalizedUsername = Normalize(username);
        var normalizedEmail = Normalize(email);
        var normalizedAddress = Normalize(address);
        if (!string.Equals(normalizedUsername, Normalize(current.Username), StringComparison.Ordinal)) fields["username"] = normalizedUsername;
        if (!string.Equals(normalizedEmail, Normalize(current.Email), StringComparison.OrdinalIgnoreCase)) fields["email"] = normalizedEmail;
        if (!string.Equals(normalizedAddress, current.Address, StringComparison.Ordinal)) fields["address"] = normalizedAddress;
        return fields;
    }

    /** Trims non-empty profile text and represents blank optional values as null. */
    static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
