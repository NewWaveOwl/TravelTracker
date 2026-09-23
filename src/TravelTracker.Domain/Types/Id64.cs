using System.Security.Cryptography;

namespace TravelTracker.Domain.Types;

/**
 * Random identifier: 64 random bits held in a long, matching bigint on
 * postgres and mssql. Not treated as an integer, just a chunk of bytes.
 * Casts to both long and ulong are valid views of the same bits.
 * ToString is 16 lowercase hex chars, e.g. "aef19c2e04d73b15".
 */
public readonly struct Id64 : IEquatable<Id64>
{
    private readonly long value;

    public Id64(long value) => this.value = value;

    public Id64(ulong value) => this.value = unchecked((long)value);

    public static Id64 New() => new(BitConverter.ToInt64(RandomNumberGenerator.GetBytes(8)));

    public bool Equals(Id64 other) => value == other.value;
    public override bool Equals(object? obj) => obj is Id64 other && Equals(other);
    public override int GetHashCode() => value.GetHashCode();
    public static bool operator ==(Id64 left, Id64 right) => left.Equals(right);
    public static bool operator !=(Id64 left, Id64 right) => !left.Equals(right);

    public static explicit operator long(Id64 id) => id.value;
    public static explicit operator ulong(Id64 id) => unchecked((ulong)id.value);

    public override string ToString() => unchecked((ulong)value).ToString("x16");
}