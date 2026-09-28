using WinPilot.Automation.Capture;

namespace WinPilot.Automation.Tests.Capture;

public class SavePathValidatorTests
{
    [Fact]
    public void Null_or_blank_is_a_no_op()
    {
        Assert.True(SavePathValidator.TryNormalize(null, overwrite: false, out var normalized, out var error));
        Assert.Null(normalized);
        Assert.Equal(string.Empty, error);

        Assert.True(SavePathValidator.TryNormalize("  ", overwrite: false, out _, out _));
    }

    [Fact]
    public void Accepts_an_absolute_local_png_path()
    {
        var path = Path.Combine(Path.GetTempPath(), "winpilot-ok.png");

        Assert.True(SavePathValidator.TryNormalize(path, overwrite: false, out var normalized, out _));
        Assert.Equal(Path.GetFullPath(path), normalized);
    }

    [Fact]
    public void Rejects_relative_paths()
    {
        Assert.False(SavePathValidator.TryNormalize("shots\\a.png", overwrite: false, out _, out var error));
        Assert.Contains("absolute", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("\\\\server\\share\\a.png")]
    [InlineData("\\\\?\\C:\\Temp\\a.png")]
    [InlineData("\\\\.\\C:\\Temp\\a.png")]
    public void Rejects_unc_and_device_paths(string path)
    {
        Assert.False(SavePathValidator.TryNormalize(path, overwrite: false, out _, out var error));
        Assert.Contains("UNC", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_non_png_extensions()
    {
        var path = Path.Combine(Path.GetTempPath(), "shot.jpg");

        Assert.False(SavePathValidator.TryNormalize(path, overwrite: false, out _, out var error));
        Assert.Contains(".png", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_a_missing_directory()
    {
        var path = Path.Combine(Path.GetTempPath(), "winpilot-missing-dir-" + Guid.NewGuid().ToString("N"), "a.png");

        Assert.False(SavePathValidator.TryNormalize(path, overwrite: false, out _, out var error));
        Assert.Contains("directory", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Existing_files_require_overwrite()
    {
        var path = Path.Combine(Path.GetTempPath(), $"winpilot-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, [1, 2, 3]);
        try
        {
            Assert.False(SavePathValidator.TryNormalize(path, overwrite: false, out _, out var error));
            Assert.Contains("overwrite", error, StringComparison.OrdinalIgnoreCase);

            Assert.True(SavePathValidator.TryNormalize(path, overwrite: true, out var normalized, out _));
            Assert.Equal(Path.GetFullPath(path), normalized);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Saving_a_png_roundtrips_byte_identical()
    {
        var path = Path.Combine(Path.GetTempPath(), $"winpilot-{Guid.NewGuid():N}.png");
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];
        try
        {
            CaptureService.SavePng(bytes, path);

            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
