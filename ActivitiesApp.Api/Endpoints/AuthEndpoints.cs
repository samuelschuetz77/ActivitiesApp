using ActivitiesApp.Infrastructure.Data;
using ActivitiesApp.Infrastructure.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ActivitiesApp.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapPost("/register", async (RegisterRequest request,
            UserManager<ApplicationUser> userManager,
            IConfiguration config) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return Results.BadRequest(new { error = "Email and password are required." });

            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                DisplayName = request.DisplayName ?? request.Email,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
                return Results.BadRequest(new { error = string.Join("; ", result.Errors.Select(e => e.Description)) });

            await userManager.AddToRoleAsync(user, "User");
            var roles = await userManager.GetRolesAsync(user);
            var token = GenerateJwtToken(user, roles, config);

            return Results.Ok(new AuthResponse
            {
                Token = token,
                UserId = user.Id,
                Email = user.Email!,
                DisplayName = user.DisplayName,
                Roles = roles.ToList()
            });
        });

        auth.MapPost("/login", async (LoginRequest request,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IConfiguration config) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user == null)
                return Results.Unauthorized();

            var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false);
            if (!result.Succeeded)
                return Results.Unauthorized();

            var roles = await userManager.GetRolesAsync(user);
            var token = GenerateJwtToken(user, roles, config);

            return Results.Ok(new AuthResponse
            {
                Token = token,
                UserId = user.Id,
                Email = user.Email!,
                DisplayName = user.DisplayName,
                Roles = roles.ToList()
            });
        });

        auth.MapGet("/me", async (HttpContext httpContext, UserManager<ApplicationUser> userManager) =>
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Results.Unauthorized();

            var user = await userManager.FindByIdAsync(userId);
            if (user is null) return Results.NotFound();

            var roles = await userManager.GetRolesAsync(user);

            return Results.Ok(new UserProfile
            {
                UserId = user.Id,
                Email = user.Email!,
                DisplayName = user.DisplayName,
                ProfilePictureUrl = user.ProfilePictureUrl,
                Roles = roles.ToList()
            });
        }).RequireAuthorization();

        auth.MapPut("/profile", async (UpdateProfileRequest request,
            HttpContext httpContext,
            UserManager<ApplicationUser> userManager) =>
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Results.Unauthorized();

            var user = await userManager.FindByIdAsync(userId);
            if (user is null) return Results.NotFound();

            user.DisplayName = request.DisplayName;
            user.ProfilePictureUrl = request.ProfilePictureUrl;
            await userManager.UpdateAsync(user);

            return Results.Ok(new { message = "Profile updated." });
        }).RequireAuthorization();

        auth.MapGet("/my-activities", async (HttpContext httpContext, IActivityDbContext db) =>
        {
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Results.Unauthorized();

            var activities = await db.Activities
                .Where(a => a.CreatedByUserId == userId && !a.IsDeleted)
                .OrderByDescending(a => a.UpdatedAt)
                .ToListAsync();

            return Results.Ok(activities);
        }).RequireAuthorization();
    }

    private static string GenerateJwtToken(ApplicationUser user, IList<string> roles, IConfiguration config)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(config["Jwt:Key"] ?? "SuperSecretDevKey12345678901234567890"));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email!),
            new(ClaimTypes.Name, user.DisplayName),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"] ?? "ActivitiesApp",
            expires: DateTime.UtcNow.AddDays(7),
            claims: claims,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

// ─── DTOs ───
public record RegisterRequest(string Email, string Password, string? DisplayName);
public record LoginRequest(string Email, string Password);
public record AuthResponse
{
    public string Token { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> Roles { get; set; } = [];
}
public record UserProfile
{
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? ProfilePictureUrl { get; set; }
    public List<string> Roles { get; set; } = [];
}
public record UpdateProfileRequest(string DisplayName, string? ProfilePictureUrl);
