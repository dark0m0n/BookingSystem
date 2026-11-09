using System.Buffers.Binary;
using System.Security.Cryptography;

namespace TicketBooking.Infrastructure.Caching;

public sealed record SeatAvailabilityVersion(Guid Id, long Version);

public static class SeatAvailabilityRevision
{
    public static string Create(IEnumerable<SeatAvailabilityVersion> inventory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[24];
        foreach (var seat in inventory.OrderBy(seat => seat.Id))
        {
            seat.Id.TryWriteBytes(bytes[..16]);
            BinaryPrimitives.WriteInt64LittleEndian(bytes[16..], seat.Version);
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
