using ActivitiesApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ActivitiesApp.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapGet("/me", (HttpContext httpContext) =>
        {
            var oid = httpContext.User.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
                   ?? httpContext.User.FindFirstValue("oid");
            var email = httpContext.User.FindFirstValue("preferred_username")
                     ?? httpContext.User.FindFirstValue(ClaimTypes.Email)
                     ?? "";
            var name = httpContext.User.FindFirstValue("name")
                    ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
                    ?? email;

            if (oid is null) return Results.Unauthorized();

            return Results.Ok(new UserProfile
            {
                UserId = oid,
                Email = email,
                DisplayName = name
            });
        }).RequireAuthorization();

        auth.MapGet("/my-activities", async (HttpContext httpContext, IActivityDbContext db) =>
        {
            var oid = httpContext.User.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
                   ?? httpContext.User.FindFirstValue("oid");
            if (oid is null) return Results.Unauthorized();

            var activities = await db.Activities
                .Where(a => a.CreatedByUserId == oid && !a.IsDeleted)
                .OrderByDescending(a => a.UpdatedAt)
                .ToListAsync();

            return Results.Ok(activities);
        }).RequireAuthorization();
    }
}

// ─── DTOs ───
public record UserProfile
{
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
}
