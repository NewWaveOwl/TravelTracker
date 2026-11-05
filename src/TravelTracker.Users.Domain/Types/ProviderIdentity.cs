namespace TravelTracker.Users.Domain.Types;

/**
 * Represents the validated issuer and subject pair for an identity provider.
 */
public readonly record struct ProviderIdentity
{
    public string Issuer { get; }
    public string Subject { get; }

    /**
     * Creates an identity from a nonblank issuer and subject.
     * @param issuer The validated identity-provider issuer.
     * @param subject The validated subject within that issuer.
     */
    public ProviderIdentity(string issuer, string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        Issuer = issuer;
        Subject = subject;
    }

    /**
     * Indicates whether this identity contains both validated components.
     * @return True when issuer and subject are nonblank.
     */
    public bool IsValid => !string.IsNullOrWhiteSpace(Issuer) && !string.IsNullOrWhiteSpace(Subject);
}
