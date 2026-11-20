using System.ComponentModel.DataAnnotations;
using TravelTracker.Frontend.Api.Users;

namespace TravelTracker.Frontend.Features.Users;

/** Editable values initialized from the server profile; username, role and status are intentionally absent. */
public sealed class ProfileEditModel
{
    readonly SelfUserProfile original;

    /**
     * Creates an editable form model from the confirmed caller profile.
     * @param profile The server-confirmed profile used as the unchanged-field baseline.
     */
    public ProfileEditModel(SelfUserProfile profile)
    {
        original = profile;
        Address = profile.Address ?? "";
    }

    [StringLength(500, ErrorMessage = "Address must be 500 characters or fewer.")]
    public string Address { get; set; }

    /** true when the address differs from the confirmed profile, using the same rules as the PATCH body. */
    public bool HasChanges => new UpdateSelfProfileRequest(Address).ChangedFields(original).Count > 0;
}
