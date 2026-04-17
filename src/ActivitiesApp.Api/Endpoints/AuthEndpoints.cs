using ActivitiesApp.Infrastructure.Data;
using ActivitiesApp.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ActivitiesApp.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapGet("/me", async (HttpContext httpContext, IActivityDbContext db) =>
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

            var settings = await db.UserSettings.FirstOrDefaultAsync(u => u.UserId == oid);

            return Results.Ok(new UserProfile
            {
                UserId = oid,
                Email = email,
                DisplayName = name,
                ProfilePictureUrl = settings?.ProfilePictureUrl
            });
        }).RequireAuthorization();

        auth.MapPut("/me/settings", async (HttpContext httpContext, IActivityDbContext db, UserSettingsRequest body) =>
        {
            var oid = httpContext.User.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
                   ?? httpContext.User.FindFirstValue("oid");
            if (oid is null) return Results.Unauthorized();

            var settings = await db.UserSettings.FirstOrDefaultAsync(u => u.UserId == oid);
            if (settings is null)
            {
                settings = new UserSettings { UserId = oid };
                db.UserSettings.Add(settings);
            }

            settings.ProfilePictureUrl = body.ProfilePictureUrl;
            await db.SaveChangesAsync();

            return Results.Ok(new UserProfile
            {
                UserId = oid,
                ProfilePictureUrl = settings.ProfilePictureUrl
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
    public string? ProfilePictureUrl { get; set; }
}

public record UserSettingsRequest
{
    public string? ProfilePictureUrl { get; set; }
}
