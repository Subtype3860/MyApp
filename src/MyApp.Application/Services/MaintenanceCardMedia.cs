using System.Text;

namespace MyApp.Application.Services;

public static class MaintenanceCardMedia
{
    public const int PhotoLimit = 4_000_000;
    public const int VideoLimit = 25_000_000;
    public static string? ContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8 || bytes.Length > VideoLimit) return null;
        if (bytes.Length <= PhotoLimit)
        {
            if (bytes[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) return "image/png";
            if (bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return "image/jpeg";
        }
        if (bytes.Length >= 12 && bytes.Slice(4,4).SequenceEqual("ftyp"u8))
        {
            var brand = Encoding.ASCII.GetString(bytes.Slice(8,4));
            if (new[] {"isom","iso2","iso3","iso4","iso5","iso6","mp41","mp42","avc1","M4V ","MSNV","dash"}.Contains(brand)) return "video/mp4";
        }
        if (bytes[..4].SequenceEqual(new byte[] {0x1a,0x45,0xdf,0xa3}) && bytes[..Math.Min(bytes.Length,512)].IndexOf("webm"u8) >= 0)
            return "video/webm";
        return null;
    }
}
