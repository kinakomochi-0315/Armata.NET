using System.Text;

namespace Armata.NET;

public static class ByteArrayExtends
{
    public static string ToHexString(this IEnumerable<byte> bytes)
    {
        return $"[{string.Join(", ", bytes.Select(item => $"0x{item:X2}"))}]";
    }

    public static string ToHexString(this IEnumerable<int> bytes)
    {
        return $"[{string.Join(", ", bytes.Select(item => $"0x{item:X2}"))}]";
    }
}