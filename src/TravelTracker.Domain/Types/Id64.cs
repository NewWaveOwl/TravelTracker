using System.Security.Cryptography;

namespace TravelTracker.Domain.Types;

/**
 * Random identifier backed by a single ulong, 64 bits in memory.
 * Postgres and mssql have no unsigned bigint, so persist via ToLong as bigint.
 * ToString is 16 lowercase hex chars, e.g. "aef19c2e04d73b15".
 */
public readonly struct Id64 : IEquatable<Id64>
{
    private readonly ulong value;

    public Id64(ulong value) => this.value = value;

    public static Id64 New() => new(BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)));

    /**
     * Bit preserving signed form for bigint columns in postgres and mssql.
     */
    public long ToLong() => unchecked((long)value);

    public static Id64 FromLong(long value) => new(unchecked((ulong)value));

    public static explicit operator ulong(Id64 id) => id.value;

    public bool Equals(Id64 other) => value == other.value;
    public override bool Equals(object? obj) => obj is Id64 other && Equals(other);
    public override int GetHashCode() => value.GetHashCode();
    public static bool operator ==(Id64 left, Id64 right) => left.Equals(right);
    public static bool operator !=(Id64 left, Id64 right) => !left.Equals(right);

    public override string ToString() => value.ToString("x16");
}