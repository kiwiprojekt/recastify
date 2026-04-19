using Recastify.Models;

namespace Recastify.Api;

public static class HealthApi
{
    public static void MapHealthApi(this WebApplication app)
    {
        app.MapGet("/api/health", () =>
        {
            var response = new HealthResponse
            {
                Status = "ok",
                Timestamp = DateTime.UtcNow
            };
            return Results.Json(response, AppJsonContext.Default.HealthResponse);
        });
    }
}
