using System.Diagnostics;
using System.Text;

namespace MyApp.Infrastructure.Media;

/// <summary>Готовое к сохранению изображение: сервер приводит фото к WebP.</summary>
public sealed record NormalizedMedia(byte[] Bytes, string ContentType, bool Converted);

/// <summary>Приводит загружаемые изображения к WebP, включая HEIC/HEIF с iPhone.</summary>
public sealed class MediaImageNormalizer
{
    /// <summary>Ограничение на исходный файл: HEIC с телефона бывает крупным.</summary>
    public const int SourceLimit = 32_000_000;
    /// <summary>Ограничение на результат конвертации.</summary>
    public const int TargetLimit = 8_000_000;

    private static readonly HashSet<string> HeicBrands = ["heic", "heix", "hevc", "hevx", "mif1", "msf1", "heis", "heim"];

    /// <summary>Определяет формат по сигнатуре файла.</summary>
    public static string Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return "image/jpeg";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        if (bytes.Length >= 12 && bytes.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            var brand = Encoding.ASCII.GetString(bytes.Slice(8, 4)).Trim();
            if (HeicBrands.Contains(brand)) return "image/heic";
        }
        return "application/octet-stream";
    }

    /// <summary>Конвертирует изображение в WebP. При отсутствии инструментов возвращает исходные данные.</summary>
    public async Task<NormalizedMedia> NormalizeAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        if (bytes.Length is 0 or > SourceLimit) return new(bytes, Detect(bytes), false);
        var type = Detect(bytes);
        if (type == "image/webp") return new(bytes, type, false);
        if (type is not ("image/jpeg" or "image/png" or "image/heic")) return new(bytes, type, false);

        var directory = Path.Combine(Path.GetTempPath(), "atlas-media");
        var id = Guid.NewGuid().ToString("N");
        var extension = type switch { "image/png" => ".png", "image/heic" => ".heic", _ => ".jpg" };
        var input = Path.Combine(directory, id + extension);
        var source = input;
        var converted = Path.Combine(directory, id + "-heic.jpg");
        var output = Path.Combine(directory, id + ".webp");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(input, bytes, cancellationToken);
            if (type == "image/heic")
            {
                if (!await RunAsync("heif-convert", ["-q", "90", input, converted], cancellationToken)) return Keep(bytes, type);
                source = converted;
            }
            if (!await RunAsync("cwebp", ["-quiet", "-q", "80", "-m", "4", source, "-o", output], cancellationToken)) return Keep(bytes, type);
            var result = await File.ReadAllBytesAsync(output, cancellationToken);
            if (result.Length is < 8 or > TargetLimit) return Keep(bytes, type);
            return new NormalizedMedia(result, "image/webp", true);
        }
        catch (Exception)
        {
            // Инструменты недоступны (например, локальная Windows-среда): сохраняем исходный файл.
            return Keep(bytes, type);
        }
        finally
        {
            foreach (var path in new[] { input, converted, output })
                try { if (File.Exists(path)) File.Delete(path); } catch { /* временный файл удалится позже. */ }
        }
    }

    private static NormalizedMedia Keep(byte[] bytes, string type) => new(bytes, type, false);

    /// <summary>Запускает внешнюю утилиту без shell-подстановок и с таймаутом.</summary>
    private static async Task<bool> RunAsync(string file, string[] arguments, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(info);
            if (process is null) return false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
