namespace TestProject.Services;

/// <summary>
/// Service for handling path security, ensuring safe file and directory access within a specified root.
/// </summary>
public class PathSecurityService
{
    private readonly string _rootPath;
    private readonly int _defaultPageSize;

    public PathSecurityService(IConfiguration configuration)
    {
        var configuredRoot = configuration["FileBrowser:BrowseRoot"]
            ?? throw new InvalidOperationException("FileBrowser:BrowseRoot is not configured.");
        _rootPath = Path.GetFullPath(configuredRoot);
        _defaultPageSize = configuration.GetValue<int>("FileBrowser:DefaultPageSize", 50);
    }

    public string RootPath => _rootPath;
    public int DefaultPageSize => _defaultPageSize;

    /// <summary>
    /// Resolves a user-supplied relative path to a safe absolute path within the browse root.
    /// Returns null if the path escapes the root or is invalid.
    /// </summary>
    public string? ResolveSafePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return _rootPath;

        // Reject obviously malicious input
        if (relativePath.Contains('\0'))
            return null;

        var combined = Path.Combine(_rootPath, relativePath);
        var resolved = Path.GetFullPath(combined);

        // Ensure resolved path is within the root (with trailing separator check)
        if (!resolved.StartsWith(_rootPath + Path.DirectorySeparatorChar)
            && !string.Equals(resolved, _rootPath, StringComparison.Ordinal))
        {
            return null;
        }

        return resolved;
    }

    /// <summary>
    /// Validates a proposed filename contains no path separators or traversal.
    /// </summary>
    public bool IsValidFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (name.Length > 255)
            return false;
        if (name.Contains('\0'))
            return false;

        var invalidChars = Path.GetInvalidFileNameChars();
        return !name.Any(c => invalidChars.Contains(c));
    }
}
