using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TicketBooking.Api.Errors;
using TicketBooking.Contracts.Auth;
using TicketBooking.Contracts.Errors;
using TicketBooking.Domain.Identity;
using TicketBooking.Infrastructure.Identity;
using TicketBooking.Infrastructure.Persistence;

namespace TicketBooking.Api.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/register", RegisterAsync).AllowAnonymous()
            .WithName("Register").WithTags("Authentication")
            .Produces<UserResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem().ProducesProblem(StatusCodes.Status409Conflict);
        endpoints.MapPost("/api/auth/login", LoginAsync).AllowAnonymous()
            .WithName("Login").WithTags("Authentication")
            .Produces<LoginResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status401Unauthorized);
        endpoints.MapPost("/api/auth/refresh", RefreshAsync).AllowAnonymous()
            .WithName("Refresh").WithTags("Authentication")
            .Produces<LoginResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status401Unauthorized);
        endpoints.MapPost("/api/auth/logout", LogoutAsync).AllowAnonymous()
            .WithName("Logout").WithTags("Authentication")
            .Produces(StatusCodes.Status204NoContent).ProducesValidationProblem();
        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(RegisterRequest request, UserManager<ApplicationUser> users,
        TicketBookingDbContext database, HttpContext context)
    {
        DisableCaching(context);
        var errors = ValidateCredentials(request.Email, request.Password);
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 200)
        {
            errors["displayName"] = ["Display name is required and must contain at most 200 characters."];
        }
        if (errors.Count > 0)
        {
            return ValidationProblem(context, errors);
        }

        var email = request.Email!.Trim();
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = request.DisplayName!.Trim() };
        await using var transaction = await database.Database.BeginTransactionAsync(context.RequestAborted);
        IdentityResult result;
        try
        {
            result = await users.CreateAsync(user, request.Password!);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "EmailIndex" or "UserNameIndex" })
        {
            return AccountConflict(context);
        }
        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName"))
            {
                return AccountConflict(context);
            }
            errors = result.Errors.GroupBy(error => error.Code.StartsWith("Password", StringComparison.Ordinal) ? "password" : "email")
                .ToDictionary(group => group.Key, group => group.Select(error => error.Code switch
                {
                    "PasswordTooShort" => $"Password must contain at least {users.Options.Password.RequiredLength} characters.",
                    "PasswordRequiresDigit" => "Password must contain a digit.",
                    "PasswordRequiresLower" => "Password must contain a lowercase letter.",
                    "PasswordRequiresUpper" => "Password must contain an uppercase letter.",
                    "PasswordRequiresNonAlphanumeric" => "Password must contain a non-alphanumeric character.",
                    _ => "The account details do not meet the account requirements."
                }).Distinct().ToArray());
            return ValidationProblem(context, errors);
        }

        // Public registration never accepts a role from the request or assigns Admin.
        var membership = await users.AddToRoleAsync(user, RoleNames.Customer);
        if (!membership.Succeeded)
        {
            throw new InvalidOperationException("Customer membership could not be assigned.");
        }
        await transaction.CommitAsync(context.RequestAborted);
        return Results.Json(new UserResponse(user.Id, user.Email!, user.DisplayName), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn, JwtTokenIssuer tokens, RefreshTokenService refreshTokens, HttpContext context)
    {
        DisableCaching(context);
        var errors = ValidateCredentials(request.Email, request.Password);
        if (errors.Count > 0)
        {
            return ValidationProblem(context, errors);
        }
        var user = await users.FindByEmailAsync(request.Email!.Trim());
        // A password alone must never bypass an account's two-factor requirement.
        if (user is null || await users.GetTwoFactorEnabledAsync(user) ||
            !(await signIn.CheckPasswordSignInAsync(user, request.Password!, lockoutOnFailure: true)).Succeeded)
        {
            return Problem(context, 401, ErrorCodes.InvalidCredentials, "Invalid credentials", "The email or password is incorrect.");
        }

        var refresh = await refreshTokens.CreateAsync(user, context.RequestAborted);
        return Results.Ok(TokenResponse(tokens, refresh));
    }

    private static async Task<IResult> RefreshAsync(RefreshRequest request, RefreshTokenService refreshTokens,
        JwtTokenIssuer tokens, HttpContext context)
    {
        DisableCaching(context);
        if (!RefreshTokenService.IsWellFormed(request.RefreshToken))
        {
            return InvalidRefreshInput(context);
        }
        var refresh = await refreshTokens.RotateAsync(request.RefreshToken!, context.RequestAborted);
        if (refresh is null)
        {
            return Problem(context, 401, ErrorCodes.InvalidRefreshToken, "Invalid refresh token",
                "The refresh token is invalid or no longer usable. Sign in again.");
        }
        return Results.Ok(TokenResponse(tokens, refresh));
    }

    private static async Task<IResult> LogoutAsync(LogoutRequest request, RefreshTokenService refreshTokens, HttpContext context)
    {
        DisableCaching(context);
        if (!RefreshTokenService.IsWellFormed(request.RefreshToken))
        {
            return InvalidRefreshInput(context);
        }
        await refreshTokens.RevokeAsync(request.RefreshToken!, context.RequestAborted);
        return Results.NoContent();
    }

    private static IResult InvalidRefreshInput(HttpContext context) =>
        ValidationProblem(context, new Dictionary<string, string[]>
        {
            ["refreshToken"] = ["A valid refresh token is required."]
        });

    private static LoginResponse TokenResponse(JwtTokenIssuer tokens, RefreshTokenGrant refresh)
    {
        var access = tokens.Issue(refresh.User, refresh.Roles);
        return new LoginResponse(access.AccessToken, "Bearer", access.ExpiresAt,
            new UserResponse(refresh.User.Id, refresh.User.Email!, refresh.User.DisplayName), refresh.Token, refresh.ExpiresAt);
    }

    private static Dictionary<string, string[]> ValidateCredentials(string? email, string? password)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 256 || !new EmailAddressAttribute().IsValid(email.Trim()))
        {
            errors["email"] = ["A valid email address of at most 256 characters is required."];
        }
        if (string.IsNullOrEmpty(password) || password.Length > 128)
        {
            errors["password"] = ["Password is required and must contain at most 128 characters."];
        }
        return errors;
    }

    private static IResult AccountConflict(HttpContext context) =>
        Problem(context, 409, ErrorCodes.AccountAlreadyExists, "Account already exists", "An account with these details already exists.");

    private static IResult Problem(HttpContext context, int status, string code, string title, string detail)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail, Type = $"urn:ticketbooking:error:{code}" };
        problem.Extensions["code"] = code;
        ApiProblemDetails.Enrich(context, problem);
        return Results.Problem(problem);
    }

    private static IResult ValidationProblem(HttpContext context, IDictionary<string, string[]> errors)
    {
        var problem = new HttpValidationProblemDetails(errors)
        {
            Status = 400,
            Type = $"urn:ticketbooking:error:{ErrorCodes.ValidationFailed}",
            Title = "Invalid request"
        };
        problem.Extensions["code"] = ErrorCodes.ValidationFailed;
        ApiProblemDetails.Enrich(context, problem);
        return Results.Problem(problem);
    }

    private static void DisableCaching(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
}
