using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using TestProject.Models;
using TestProject.Services;

namespace TestProject.Controllers;

/// Controller to handle file browsing, downloading, uploading, searching etc
[ApiController]
[Route("api/files")]
public class FilesController : ControllerBase {
    private readonly PathSecurityService _pathService;
    private readonly ILogger<FilesController> _logger;
    private readonly string _apiKey;

    public FilesController(PathSecurityService pathService, ILogger<FilesController> logger, IConfiguration configuration) {
        _pathService = pathService;
        _logger = logger;
        _apiKey = configuration["FileBrowser:ApiKey"]
            ?? throw new InvalidOperationException("FileBrowser:ApiKey is not configured.");
    }

    /// <summary>
    /// Browse files in a directory.
    /// </summary>
    [HttpGet("browse")]
    [ProducesResponseType(typeof(BrowseResponse), 200)]
    public IActionResult Browse(
        [FromQuery] string? path,
        [FromQuery(Name = "$search")] string? search,
        [FromQuery(Name = "$orderby")] string? orderby,
        [FromQuery(Name = "$top")] int? top,
        [FromQuery(Name = "$skip")] int? skip,
        [FromQuery] int page = 1,
        [FromQuery] int? pageSize = null) {

        var resolved = _pathService.ResolveSafePath(path);
        if (resolved == null)
            return BadRequest("Invalid path.");

        if (!Directory.Exists(resolved))
            return NotFound("Directory not found.");

        // Gather all entries
        var allEntries = new List<FileEntry>();

        foreach (var dir in Directory.GetDirectories(resolved)) {
            var info = new DirectoryInfo(dir);
            allEntries.Add(new FileEntry(info.Name, true, null, info.LastWriteTimeUtc));
        }

        foreach (var file in Directory.GetFiles(resolved)) {
            var info = new FileInfo(file);
            allEntries.Add(new FileEntry(info.Name, false, info.Length, info.LastWriteTimeUtc));
        }

        var totalCount = allEntries.Count;
        var folderCount = allEntries.Count(e => e.IsDirectory);
        var fileCount = totalCount - folderCount;
        var totalSize = allEntries.Where(e => !e.IsDirectory).Sum(e => e.Size ?? 0);

        // $search — case-insensitive contains on name
        IEnumerable<FileEntry> query = allEntries;
        if (!string.IsNullOrWhiteSpace(search)) {
            var q = search.Trim();
            query = query.Where(e => e.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        // $orderby — e.g. "name desc", "size asc", "modified desc"
        // Default: directories first, then name asc
        query = ApplyOrderBy(query, orderby);

        var filteredCount = query.Count();

        // Paging via $top/$skip or page/pageSize
        // Force page size max regardless to avoid excessive load
        int actualSkip, actualTop;
        if (top.HasValue || skip.HasValue) {
            actualSkip = Math.Max(skip ?? 0, 0);
            actualTop = Math.Clamp(top ?? 50, 1, 500);
        } else {
            var defaultPageSize = _pathService.DefaultPageSize;
            var size = Math.Clamp(pageSize ?? defaultPageSize, 1, 500);
            if (page < 1) page = 1;
            actualSkip = (page - 1) * size;
            actualTop = size;
        }

        var totalPages = (int)Math.Ceiling((double)filteredCount / actualTop);
        if (totalPages < 1) totalPages = 1;
        var currentPage = Math.Clamp((actualSkip / actualTop) + 1, 1, totalPages);

        var paged = query.Skip(actualSkip).Take(actualTop).ToList();

        return Ok(new BrowseResponse {
            Path = Path.GetRelativePath(_pathService.RootPath, resolved).Replace('\\', '/'),
            Page = currentPage,
            PageSize = actualTop,
            TotalCount = totalCount,
            FilteredCount = filteredCount,
            FolderCount = folderCount,
            FileCount = fileCount,
            TotalSize = totalSize,
            TotalPages = totalPages,
            Entries = paged.Select(e => new FileEntryDto {
                Name = e.Name,
                IsDirectory = e.IsDirectory,
                Size = e.Size,
                LastModified = e.LastModified
            })
        });
    }

    private static IEnumerable<FileEntry> ApplyOrderBy(IEnumerable<FileEntry> query, string? orderby) {
        if (string.IsNullOrWhiteSpace(orderby)) {
            return query.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
        }

        var parts = orderby.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var field = parts[0].ToLowerInvariant();
        var desc = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);

        return field switch {
            "name" => desc
                ? query.OrderByDescending(e => e.IsDirectory).ThenByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase)
                : query.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            "size" => desc
                ? query.OrderByDescending(e => e.IsDirectory).ThenByDescending(e => e.Size ?? 0)
                : query.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Size ?? 0),
            "modified" or "lastmodified" => desc
                ? query.OrderByDescending(e => e.IsDirectory).ThenByDescending(e => e.LastModified)
                : query.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.LastModified),
            "type" => desc
                ? query.OrderBy(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                : query.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            _ => query.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        };
    }

    private record FileEntry(string Name, bool IsDirectory, long? Size, DateTime LastModified);

    /// <summary>
    /// Download a file.
    /// </summary>
    [HttpGet("download")]
    public IActionResult Download([FromQuery] string path) {
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest("Path is required.");

        var resolved = _pathService.ResolveSafePath(path);
        if (resolved == null)
            return BadRequest("Invalid path.");

        if (!System.IO.File.Exists(resolved))
            return NotFound("File not found.");

        var fileName = Path.GetFileName(resolved);
        var stream = new FileStream(resolved, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, "application/octet-stream", fileName);
    }

    /// <summary>
    /// Upload a file.
    /// </summary>
    [HttpPost("upload")]
    [ProducesResponseType(typeof(UploadResponse), 200)]
    public async Task<IActionResult> Upload([FromQuery] string? path, IFormFile file) {
        if (file == null || file.Length == 0)
            return BadRequest("No file provided.");

        if (!_pathService.IsValidFileName(file.FileName))
            return BadRequest("Invalid file name.");

        var resolved = _pathService.ResolveSafePath(path);
        if (resolved == null)
            return BadRequest("Invalid path.");

        if (!Directory.Exists(resolved))
            return NotFound("Target directory not found.");

        var targetFile = Path.Combine(resolved, file.FileName);

        // Re-validate the final path
        var finalCheck = _pathService.ResolveSafePath(
            Path.GetRelativePath(_pathService.RootPath, targetFile));
        if (finalCheck == null)
            return BadRequest("Invalid target path.");

        await using var stream = new FileStream(targetFile, FileMode.Create);
        await file.CopyToAsync(stream);

        _logger.LogInformation("File uploaded: {Path}", targetFile);
        return Ok(new UploadResponse { Name = file.FileName, Size = file.Length });
    }

    /// <summary>
    /// Rename a file or directory.
    /// </summary>
    [HttpPatch("rename")]
    [ProducesResponseType(typeof(RenameResponse), 200)]
    public IActionResult Rename([FromBody] RenameRequest request) {
        if (string.IsNullOrWhiteSpace(request.Path) || string.IsNullOrWhiteSpace(request.NewName))
            return BadRequest("Path and newName are required.");

        if (!_pathService.IsValidFileName(request.NewName))
            return BadRequest("Invalid new name.");

        var resolved = _pathService.ResolveSafePath(request.Path);
        if (resolved == null)
            return BadRequest("Invalid path.");

        var parentDir = Path.GetDirectoryName(resolved);
        if (parentDir == null)
            return BadRequest("Cannot rename root.");

        var newPath = Path.Combine(parentDir, request.NewName);

        // Validate new path is still within root
        var newPathCheck = _pathService.ResolveSafePath(
            Path.GetRelativePath(_pathService.RootPath, newPath));
        if (newPathCheck == null)
            return BadRequest("Invalid target path.");

        if (Directory.Exists(resolved)) {
            Directory.Move(resolved, newPath);
        } else if (System.IO.File.Exists(resolved)) {
            System.IO.File.Move(resolved, newPath);
        } else {
            return NotFound("File or directory not found.");
        }

        _logger.LogInformation("Renamed: {Old} -> {New}", resolved, newPath);
        return Ok(new RenameResponse { Name = request.NewName });
    }

    /// <summary>
    /// Delete a file or empty directory.
    /// </summary>
    [HttpDelete("delete")]
    [ProducesResponseType(typeof(DeleteResponse), 200)]
    public IActionResult Delete([FromQuery] string path) {
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest("Path is required.");

        var resolved = _pathService.ResolveSafePath(path);
        if (resolved == null)
            return BadRequest("Invalid path.");

        // Prevent deleting the root directory
        if (resolved == _pathService.RootPath)
            return BadRequest("Cannot delete root directory.");

        bool wasDirectory;
        string name;

        if (Directory.Exists(resolved)) {
            wasDirectory = true;
            name = new DirectoryInfo(resolved).Name;
            Directory.Delete(resolved, false); // non-recursive, empty dirs only
        } else if (System.IO.File.Exists(resolved)) {
            wasDirectory = false;
            name = Path.GetFileName(resolved);
            System.IO.File.Delete(resolved);
        } else {
            return NotFound("File or directory not found.");
        }

        _logger.LogInformation("Deleted: {Path}", resolved);
        return Ok(new DeleteResponse { Name = name, WasDirectory = wasDirectory });
    }

    /// <summary>
    /// Move a file or directory to a new location.
    /// </summary>
    [HttpPost("move")]
    [ProducesResponseType(typeof(MoveResponse), 200)]
    public IActionResult Move([FromBody] MoveRequest request) {
        if (string.IsNullOrWhiteSpace(request.Path) || string.IsNullOrWhiteSpace(request.Destination))
            return BadRequest("Path and destination are required.");

        var sourceResolved = _pathService.ResolveSafePath(request.Path);
        if (sourceResolved == null)
            return BadRequest("Invalid source path.");

        var destResolved = _pathService.ResolveSafePath(request.Destination);
        if (destResolved == null)
            return BadRequest("Invalid destination path.");

        if (!Directory.Exists(destResolved))
            return NotFound("Destination directory not found.");

        var isDir = Directory.Exists(sourceResolved);
        var isFile = System.IO.File.Exists(sourceResolved);
        if (!isDir && !isFile)
            return NotFound("Source file or directory not found.");

        var name = isDir ? new DirectoryInfo(sourceResolved).Name : Path.GetFileName(sourceResolved);
        var targetPath = Path.Combine(destResolved, name);

        // Validate target is within root
        var targetCheck = _pathService.ResolveSafePath(
            Path.GetRelativePath(_pathService.RootPath, targetPath));
        if (targetCheck == null)
            return BadRequest("Invalid target path.");

        if (Directory.Exists(targetPath) || System.IO.File.Exists(targetPath))
            return Conflict("An item with that name already exists in the destination.");

        if (isDir) {
            Directory.Move(sourceResolved, targetPath);
        } else {
            System.IO.File.Move(sourceResolved, targetPath);
        }

        var relPath = Path.GetRelativePath(_pathService.RootPath, targetPath).Replace('\\', '/');
        _logger.LogInformation("Moved: {Old} -> {New}", sourceResolved, targetPath);
        return Ok(new MoveResponse { Name = name, NewPath = relPath });
    }

    /// <summary>
    /// Copy a file to a new location.
    /// </summary>
    [HttpPost("copy")]
    [ProducesResponseType(typeof(CopyResponse), 200)]
    public IActionResult Copy([FromBody] CopyRequest request) {
        if (string.IsNullOrWhiteSpace(request.Path) || string.IsNullOrWhiteSpace(request.Destination))
            return BadRequest("Path and destination are required.");

        var sourceResolved = _pathService.ResolveSafePath(request.Path);
        if (sourceResolved == null)
            return BadRequest("Invalid source path.");

        if (!System.IO.File.Exists(sourceResolved))
            return NotFound("Source file not found. Only files can be copied.");

        var destResolved = _pathService.ResolveSafePath(request.Destination);
        if (destResolved == null)
            return BadRequest("Invalid destination path.");

        if (!Directory.Exists(destResolved))
            return NotFound("Destination directory not found.");

        var name = Path.GetFileName(sourceResolved);
        var targetPath = Path.Combine(destResolved, name);

        // Validate target is within root
        var targetCheck = _pathService.ResolveSafePath(
            Path.GetRelativePath(_pathService.RootPath, targetPath));
        if (targetCheck == null)
            return BadRequest("Invalid target path.");

        if (System.IO.File.Exists(targetPath))
            return Conflict("A file with that name already exists in the destination.");

        System.IO.File.Copy(sourceResolved, targetPath);

        var relPath = Path.GetRelativePath(_pathService.RootPath, targetPath).Replace('\\', '/');
        _logger.LogInformation("Copied: {Old} -> {New}", sourceResolved, targetPath);
        return Ok(new CopyResponse { Name = name, NewPath = relPath });
    }

    /// <summary>
    /// Share a file or directory.
    /// </summary>
    [HttpPost("share")]
    [ProducesResponseType(typeof(ShareResponse), 200)]
    public IActionResult Share([FromBody] ShareRequest request) {
        if (string.IsNullOrWhiteSpace(request.Path))
            return BadRequest("Path is required.");

        var resolved = _pathService.ResolveSafePath(request.Path);
        if (resolved == null)
            return BadRequest("Invalid path.");

        var isDir = Directory.Exists(resolved);
        var isFile = System.IO.File.Exists(resolved);
        if (!isDir && !isFile)
            return NotFound("File or directory not found.");

        // Direct link (requires API key)
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        string directLink;
        if (isDir) {
            var rel = Path.GetRelativePath(_pathService.RootPath, resolved).Replace('\\', '/');
            directLink = rel == "." ? $"{baseUrl}/browse" : $"{baseUrl}/browse/{Uri.EscapeDataString(rel).Replace("%2F", "/")}";
        } else {
            directLink = $"{baseUrl}/api/files/download?path={Uri.EscapeDataString(request.Path)}";
        }

        // Expiring signed link (files only)
        string? expiringLink = null;
        if (isFile) {
            var minutes = Math.Clamp(request.ExpiresInMinutes ?? 60, 1, 43200); // max 30 days
            var expires = DateTimeOffset.UtcNow.AddMinutes(minutes).ToUnixTimeSeconds().ToString();
            var sig = ComputeHmac(request.Path, expires);
            expiringLink = $"{baseUrl}/api/files/shared?path={Uri.EscapeDataString(request.Path)}&expires={expires}&sig={Uri.EscapeDataString(sig)}";
        }

        return Ok(new ShareResponse {
            DirectLink = directLink,
            ExpiringLink = expiringLink,
            IsDirectory = isDir,
            ExpiresAt = isFile ? DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(request.ExpiresInMinutes ?? 60, 1, 43200)).ToString("o") : null
        });
    }

    /// <summary>
    /// Access/redeem a shared file via an expiring signed link.
    /// </summary>
    [HttpGet("shared")]
    public IActionResult Shared([FromQuery] string path, [FromQuery] string expires, [FromQuery] string sig) {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(expires) || string.IsNullOrWhiteSpace(sig))
            return BadRequest("Missing parameters.");

        // Validate expiry
        if (!long.TryParse(expires, out var expUnix))
            return BadRequest("Invalid expiry.");

        var expTime = DateTimeOffset.FromUnixTimeSeconds(expUnix);
        if (DateTimeOffset.UtcNow > expTime)
            return StatusCode(410, "Link has expired.");

        // Validate signature
        var expected = ComputeHmac(path, expires);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(sig))) {
            return StatusCode(403, "Invalid signature.");
        }

        // Validate path
        var resolved = _pathService.ResolveSafePath(path);
        if (resolved == null)
            return BadRequest("Invalid path.");

        if (!System.IO.File.Exists(resolved))
            return NotFound("File not found.");

        _logger.LogInformation("Shared link accessed: {Path}", path);
        var fileName = Path.GetFileName(resolved);
        var stream = new FileStream(resolved, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, "application/octet-stream", fileName);
    }

    /// <summary>
    /// Compute the HMAC signature for a shared link.
    /// </summary>
    private string ComputeHmac(string path, string expires)
    {
        var message = $"{path}|{expires}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_apiKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToBase64String(hash);
    }

}
