namespace TestProject.Models;

public record FileEntryDto {
    public string Name { get; init; } = "";
    public bool IsDirectory { get; init; }
    public long? Size { get; init; }
    public DateTime LastModified { get; init; }
}

public record BrowseResponse {
    public string Path { get; init; } = "";
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int FilteredCount { get; init; }
    public int FolderCount { get; init; }
    public int FileCount { get; init; }
    public long TotalSize { get; init; }
    public int TotalPages { get; init; }
    public IEnumerable<FileEntryDto> Entries { get; init; } = [];
}

public record UploadResponse {
    public string Name { get; init; } = "";
    public long Size { get; init; }
}

public record RenameResponse {
    public string Name { get; init; } = "";
}

public record ShareResponse {
    public string DirectLink { get; init; } = "";
    public string? ExpiringLink { get; init; }
    public bool IsDirectory { get; init; }
    public string? ExpiresAt { get; init; }
}

public record ShareRequest {
    public string Path { get; init; } = "";
    public int? ExpiresInMinutes { get; init; }
}

public record RenameRequest {
    public string Path { get; init; } = "";
    public string NewName { get; init; } = "";
}

public record DeleteResponse {
    public string Name { get; init; } = "";
    public bool WasDirectory { get; init; }
}

public record MoveRequest {
    public string Path { get; init; } = "";
    public string Destination { get; init; } = "";
}

public record MoveResponse {
    public string Name { get; init; } = "";
    public string NewPath { get; init; } = "";
}

public record CopyRequest {
    public string Path { get; init; } = "";
    public string Destination { get; init; } = "";
}

public record CopyResponse {
    public string Name { get; init; } = "";
    public string NewPath { get; init; } = "";
}

public record ApiErrorResponse {
    public bool Error { get; init; } = true;
    public int Status { get; init; }
    public string Message { get; init; } = "";
    public string? Detail { get; init; }
}
