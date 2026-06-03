# turbo-chainsaw

A file and directory browsing single-page application built with a .NET 8 Web API backend and a vanilla JavaScript frontend. No frameworks, no build tools — just static HTML/CSS/JS served by ASP.NET Core.

## Features

### SPA Frontend
- **Browse** directories with breadcrumb navigation and deep-linkable URLs (`/browse/path/to/dir`)
- **Search** files by name with debounced server-side filtering
- **Sort** by name, size, or date (ascending/descending) via toolbar dropdown
- **Paginate** large directories server-side with configurable page sizes
- **Upload** files via button or drag-and-drop
- **Rename** files and folders inline
- **Delete** files and empty directories with confirmation
- **Move** files and directories to a different folder
- **Copy** files to a different folder
- **Share** via direct links (requires API key) or time-limited expiring links (HMAC-signed, no key needed)
- **Directory stats** showing file count, folder count, and total size
- **Toast notifications** for action feedback

### Web API
All endpoints live under `/api/files` and are secured with an API key (`X-Api-Key` header or `apikey` query param).

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/files/browse` | List directory contents with OData-style `$search`, `$orderby`, `$top`, `$skip` support |
| GET | `/api/files/download` | Download a file |
| POST | `/api/files/upload` | Upload a file (multipart form) |
| PATCH | `/api/files/rename` | Rename a file or directory |
| DELETE | `/api/files/delete` | Delete a file or empty directory |
| POST | `/api/files/move` | Move a file or directory to a different folder |
| POST | `/api/files/copy` | Copy a file to a different folder |
| POST | `/api/files/share` | Generate direct and/or expiring share links |
| GET | `/api/files/shared` | Redeem an expiring share link (HMAC-authenticated, no API key required) |

### Swagger / OpenAPI
Swagger UI is available at `/swagger` for interactive API exploration. The spec includes typed request/response schemas and the `X-Api-Key` security definition, making it straightforward to import into Postman or other API clients.

> **Note on Swashbuckle:** This is the only third-party NuGet package used. It was a deliberate choice — Swagger/OpenAPI support is effectively a force multiplier for anyone consuming this API. It auto-generates a live, testable API spec that can be imported directly into Postman, used for client code generation, or referenced as documentation. The development-time ROI of a single package that eliminates the need to manually write and maintain API docs far outweighs the cost of the dependency.

### Known Limitations
- **Copy** is file-only (not directories) and requires a *different* destination folder — it is not a duplicate-in-place tool. If you need to duplicate a file in the same directory, rename it afterward.
- **Delete** on directories only works if the directory is empty (non-recursive), preventing accidental bulk data loss.

### Security
- API key authentication on all `/api/*` routes (except HMAC-signed share links)
- Path traversal protection via canonicalization and root-boundary checks
- Null byte rejection in paths
- File name validation
- HMAC-SHA256 signed expiring links with constant-time signature comparison

## Configuration

Settings in `appsettings.json` under `FileBrowser`:

```json
{
  "FileBrowser": {
    "BrowseRoot": "/tmp/browse",
    "ApiKey": "change_me",
    "MaxUploadSizeMb": 10,
    "DefaultPageSize": 50
  }
}
```

| Key | Description |
|-----|-------------|
| `BrowseRoot` | Root directory exposed for browsing |
| `ApiKey` | API key for authentication |
| `MaxUploadSizeMb` | Max upload size in MB |
| `DefaultPageSize` | Default page size for browse results |

## Running

```bash
dotnet run
```

The app serves on the URLs configured in `Properties/launchSettings.json`. Open the root URL to use the SPA, or `/swagger` for the API docs.

## Tests

Unit tests live in `Tests/` and target the most critical business logic — path security and input validation — rather than aiming for broad coverage.

```bash
dotnet test
```

| Area | What's covered |
|------|---------------|
| `ResolveSafePath` | Null/empty input, valid subpaths, `../` traversal attacks (4 variants), null byte injection, safe relative `../` within root |
| `IsValidFileName` | Valid names, empty/whitespace, path separators, null bytes, 255-char boundary |
| Configuration | `RootPath` and `DefaultPageSize` read correctly from config |

## Future Considerations

- **Request and query caching** — Add response caching (e.g. `ETag`/`If-None-Match`, in-memory cache for directory listings) to reduce redundant filesystem I/O and improve performance under load
- **SSO integration** — Replace API key auth with an SSO provider (OAuth 2.0 / OpenID Connect) for enterprise environments with centralized identity management
- **In-page file preview** — Render common file types directly in the browser (text, markdown, images, PDF) instead of requiring a download
- **Multi-tenant API keys** — Map API keys to isolated root directories, enabling per-tenant access control where each key scopes browsing to its own folder hierarchy
- **Directory Operations** — Allow for creating a new folder. Right now renames, moves and deletes are supported. 
