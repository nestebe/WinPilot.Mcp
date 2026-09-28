namespace WinPilot.Automation.Capture;

/// <summary>
/// Validates screenshot save paths. Rules preserved from the original server: absolute local
/// <c>.png</c> paths only, no UNC or device paths, and no silent overwrite of existing files.
/// </summary>
internal static class SavePathValidator
{
    /// <summary>
    /// Normalizes and validates <paramref name="savePath"/>. A null or blank path is a valid no-op.
    /// </summary>
    public static bool TryNormalize(string? savePath, bool overwrite, out string? normalizedPath, out string error)
    {
        normalizedPath = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(savePath))
        {
            return true;
        }

        if (!Path.IsPathFullyQualified(savePath))
        {
            error = $"savePath must be an absolute local path: {savePath}";
            return false;
        }

        if (savePath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            error = "savePath must be a local drive path; UNC and device paths are not allowed";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(savePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"savePath is invalid: {exception.Message}";
            return false;
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            error = $"savePath must end with .png: {savePath}";
            return false;
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            error = $"savePath directory does not exist: {directory}";
            return false;
        }

        if (File.Exists(fullPath) && !overwrite)
        {
            error = $"File already exists (pass overwrite: true to replace): {fullPath}";
            return false;
        }

        normalizedPath = fullPath;
        return true;
    }
}
