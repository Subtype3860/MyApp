namespace MyApp.Infrastructure.Storage;

/// <summary>
/// Validates and opens the configured component issue form, which is an
/// external, deployment-provided PDF asset (not stored in the Git repository).
/// </summary>
public static class ComponentPdfTemplateFile
{
    public static FileStream OpenRead(string configuredPath, string contentRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);

        var path = Path.GetFullPath(configuredPath, contentRoot);
        var stream = System.IO.File.OpenRead(path);
        try
        {
            Span<byte> signature = stackalloc byte[5];
            if (stream.Read(signature) != signature.Length ||
                !signature.SequenceEqual("%PDF-"u8))
            {
                throw new InvalidDataException(
                    "Configured component issue template does not contain a PDF header.");
            }

            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
