using System.Security.Claims;
using GrotsPizzeria.Server.Auth;
using GrotsPizzeria.Shared;
using Microsoft.AspNetCore.Identity;

namespace GrotsPizzeria.Server.Endpoints;

/// <summary>
/// Thin JSON endpoints over ASP.NET Core Identity: <see cref="UserManager{TUser}"/> and
/// <see cref="SignInManager{TUser}"/> do all of the password hashing, validation, lockout and cookie work.
/// </summary>
internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/register", RegisterAsync);
        group.MapPost("/login", LoginAsync);
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();
        group.MapGet("/me", (ClaimsPrincipal user) => user.Identity?.IsAuthenticated is true
            ? new UserInfo(true, user.Identity.Name, user.FindFirstValue(ClaimTypes.GivenName))
            : new UserInfo(false));

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        Telemetry telemetry,
        ILogger<ApplicationUser> logger)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? username : request.DisplayName.Trim();

        if (displayName.Length > 50)
        {
            return Problem(StatusCodes.Status400BadRequest, "Invalid display name", "Display names are at most 50 characters.");
        }

        var user = new ApplicationUser { UserName = username, DisplayName = displayName };
        var result = await users.CreateAsync(user, request.Password ?? string.Empty);

        if (!result.Succeeded)
        {
            telemetry.RecordAuth("register", success: false);

            return result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateUserName))
                ? Problem(StatusCodes.Status409Conflict, "Username taken", "That username already exists.")
                : Problem(StatusCodes.Status400BadRequest, "Registration failed", string.Join(' ', result.Errors.Select(e => e.Description)));
        }

        await users.AddClaimAsync(user, new Claim(ClaimTypes.GivenName, displayName));
        await signIn.SignInAsync(user, isPersistent: true);

        telemetry.RecordAuth("register", success: true);
        logger.LogInformation("Registered user {user.name}", username);

        return Results.Json(new UserInfo(true, username, displayName), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        SignInManager<ApplicationUser> signIn,
        Telemetry telemetry,
        ILogger<ApplicationUser> logger)
    {
        var result = await signIn.PasswordSignInAsync(
            request.Username?.Trim() ?? string.Empty,
            request.Password ?? string.Empty,
            isPersistent: true,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            telemetry.RecordAuth("login", success: false);
            logger.LogWarning("Failed login attempt for {user.name} (locked out: {grot.auth.locked_out})", request.Username, result.IsLockedOut);

            return result.IsLockedOut
                ? Problem(StatusCodes.Status429TooManyRequests, "Account locked", "Too many failed attempts. Try again in a few minutes.")
                : Problem(StatusCodes.Status401Unauthorized, "Invalid credentials", "The username or password is incorrect.");
        }

        telemetry.RecordAuth("login", success: true);
        logger.LogInformation("User {user.name} logged in", request.Username);

        // PasswordSignInAsync has no principal to read back from, so ask the cookie-issuing manager for it.
        var user = await signIn.UserManager.FindByNameAsync(request.Username!.Trim());
        return Results.Ok(new UserInfo(true, user!.UserName, user.DisplayName));
    }

    private static async Task<IResult> LogoutAsync(SignInManager<ApplicationUser> signIn)
    {
        await signIn.SignOutAsync();
        return Results.NoContent();
    }

    private static IResult Problem(int status, string title, string detail) =>
        Results.Json(new ProblemResponse(title, status, detail), statusCode: status);
}
