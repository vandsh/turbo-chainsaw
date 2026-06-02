using TestProject.Filters;
using TestProject.Middleware;
using TestProject.Services;

namespace TestProject {
    public class Program {
        public static void Main(string[] args) {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers(opts => {
                opts.Filters.Add<ApiExceptionFilter>();
            });
            builder.Services.AddSingleton<PathSecurityService>();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c => {
                c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo {
                    Title = "File Browser API",
                    Version = "v1"
                });
                c.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme {
                    In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                    Name = "X-Api-Key",
                    Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
                    Description = "API key via X-Api-Key header"
                });
                c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement {
                    {
                        new Microsoft.OpenApi.Models.OpenApiSecurityScheme {
                            Reference = new Microsoft.OpenApi.Models.OpenApiReference {
                                Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                                Id = "ApiKey"
                            }
                        },
                        Array.Empty<string>()
                    }
                });
            });

            // Configure upload size limit
            var maxUploadMb = builder.Configuration.GetValue<int>("FileBrowser:MaxUploadSizeMb", 100);
            builder.WebHost.ConfigureKestrel(opts => {
                opts.Limits.MaxRequestBodySize = maxUploadMb * 1024L * 1024L;
            });

            var app = builder.Build();

            app.UseMiddleware<ApiKeyMiddleware>();

            app.UseSwagger();
            app.UseSwaggerUI(c => {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "File Browser API v1");
            });

            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.MapControllers();

            // SPA fallback — non-API, non-file, non-swagger routes serve index.html
            app.MapFallback(context => {
                var reqPath = context.Request.Path.Value ?? "";
                if (reqPath.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)) {
                    context.Response.StatusCode = 404;
                    return Task.CompletedTask;
                }
                context.Request.Path = "/index.html";
                return Results.File(
                    Path.Combine(app.Environment.WebRootPath, "index.html"),
                    "text/html"
                ).ExecuteAsync(context);
            });

            app.Run();
        }
    }
}