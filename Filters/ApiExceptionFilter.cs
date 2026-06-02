using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TestProject.Filters;

/// <summary>
/// Exception filter for handling API exceptions and returning structured error responses.
/// </summary>
public class ApiExceptionFilter : IExceptionFilter
{
    private readonly ILogger<ApiExceptionFilter> _logger;
    private readonly IHostEnvironment _env;

    public ApiExceptionFilter(ILogger<ApiExceptionFilter> logger, IHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    /// <summary>
    /// Handles exceptions and returns structured error responses.
    /// </summary>
    public void OnException(ExceptionContext context)
    {
        _logger.LogError(context.Exception, "Unhandled exception on {Method} {Path}",
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path);

        var (statusCode, message) = context.Exception switch
        {
            UnauthorizedAccessException => (HttpStatusCode.Forbidden, "Access denied."),
            FileNotFoundException => (HttpStatusCode.NotFound, "Resource not found."),
            DirectoryNotFoundException => (HttpStatusCode.NotFound, "Directory not found."),
            IOException ex when ex.Message.Contains("disk full", StringComparison.OrdinalIgnoreCase) => (HttpStatusCode.InsufficientStorage, "Insufficient storage."),
            IOException => (HttpStatusCode.Conflict, "File operation failed."),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        var response = new
        {
            error = true,
            status = (int)statusCode,
            message,
            detail = _env.IsDevelopment() ? context.Exception.ToString() : null
        };

        context.Result = new ObjectResult(response) { StatusCode = (int)statusCode };
        context.ExceptionHandled = true;
    }
}
