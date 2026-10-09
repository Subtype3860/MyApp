using MyApp.Infrastructure.Storage;

namespace MyApp.Infrastructure.Tests;

public sealed class ComponentPdfTemplateFileTests
{
    [Xunit.Fact]
    public void Relative_template_path_is_resolved_against_application_directory()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var subdirectory = Path.Combine(directory, "templates");
            Directory.CreateDirectory(subdirectory);
            var expected = Path.Combine(subdirectory, "issue.pdf");
            System.IO.File.WriteAllBytes(expected, "%PDF-1.7\n"u8.ToArray());

            using var pdf = ComponentPdfTemplateFile.OpenRead(
                Path.Combine("templates", "issue.pdf"), directory);
            Xunit.Assert.Equal(expected, pdf.Name);
            Xunit.Assert.Equal(0L, pdf.Position);
            var signature = new byte[5];
            Xunit.Assert.Equal(5, pdf.Read(signature));
            Xunit.Assert.Equal("%PDF-"u8.ToArray(), signature);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Xunit.Fact]
    public void Absolute_template_path_is_preserved()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var absolutePath = Path.Combine(directory, "template.pdf");
            System.IO.File.WriteAllBytes(absolutePath, "%PDF-1.4\n"u8.ToArray());

            using var pdf = ComponentPdfTemplateFile.OpenRead(
                absolutePath, directory);
            Xunit.Assert.Equal(absolutePath, pdf.Name);
            Xunit.Assert.Equal(0L, pdf.Position);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Xunit.Fact]
    public void Missing_pdf_file_is_reported_as_missing()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            Xunit.Assert.Throws<FileNotFoundException>(() =>
            {
                using var _ = ComponentPdfTemplateFile.OpenRead(
                    "missing.pdf", directory);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Xunit.Fact]
    public void Html_instead_of_pdf_is_rejected()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "template.pdf");
            System.IO.File.WriteAllText(path, "<html>Unauthorized</html>");

            Xunit.Assert.Throws<InvalidDataException>(() =>
            {
                using var _ = ComponentPdfTemplateFile.OpenRead(
                    path, directory);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Xunit.Fact]
    public void Empty_template_path_is_rejected()
    {
        Xunit.Assert.Throws<ArgumentException>(() =>
        {
            using var _ = ComponentPdfTemplateFile.OpenRead(
                "", Path.GetTempPath());
        });
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(),
            "myapp-template-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
