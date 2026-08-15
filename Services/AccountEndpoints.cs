using System.Security.Claims;
using Calendar.Data;
using Calendar.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Calendar.Services;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/account/register", RegisterAsync);
        endpoints.MapPost("/account/login", LoginAsync);
        endpoints.MapPost("/account/logout", LogoutAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        HttpContext context,
        CalendarDbContext db,
        IAntiforgery antiforgery,
        IPasswordHasher<AppUser> passwordHasher)
    {
        if (!await IsValidRequestAsync(context, antiforgery)) return Results.BadRequest();
        var form = await context.Request.ReadFormAsync();
        var name = form["name"].ToString().Trim();
        var email = form["email"].ToString().Trim();
        var password = form["password"].ToString();

        if (name.Length is < 2 or > 80)
            return RedirectWithError("/register", "Enter a name between 2 and 80 characters.");
        if (!IsValidEmail(email))
            return RedirectWithError("/register", "Enter a valid email address.");
        if (password.Length < 8)
            return RedirectWithError("/register", "Password must contain at least 8 characters.");

        var normalizedEmail = NormalizeEmail(email);
        if (await db.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail))
            return RedirectWithError("/register", "An account with this email already exists.");

        var user = new AppUser { Name = name, Email = email, NormalizedEmail = normalizedEmail };
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await SignInAsync(context, user, true);
        return Results.LocalRedirect("/");
    }

    private static async Task<IResult> LoginAsync(
        HttpContext context,
        CalendarDbContext db,
        IAntiforgery antiforgery,
        IPasswordHasher<AppUser> passwordHasher)
    {
        if (!await IsValidRequestAsync(context, antiforgery)) return Results.BadRequest();
        var form = await context.Request.ReadFormAsync();
        var email = form["email"].ToString().Trim();
        var password = form["password"].ToString();
        var rememberMe = form["rememberMe"] == "on";
        var returnUrl = SafeReturnUrl(form["returnUrl"].ToString());
        var normalizedEmail = NormalizeEmail(email);
        var user = await db.Users.SingleOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail);

        if (user is null || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
            return RedirectWithError("/login", "Email or password is incorrect.", returnUrl);

        await SignInAsync(context, user, rememberMe);
        return Results.LocalRedirect(returnUrl);
    }

    private static async Task<IResult> LogoutAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!await IsValidRequestAsync(context, antiforgery)) return Results.BadRequest();
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.LocalRedirect("/login");
    }

    private static async Task SignInAsync(HttpContext context, AppUser user, bool persistent)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
        {
            IsPersistent = persistent,
            ExpiresUtc = persistent ? DateTimeOffset.UtcNow.AddDays(30) : null
        });
    }

    private static async Task<bool> IsValidRequestAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try { await antiforgery.ValidateRequestAsync(context); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }

    private static IResult RedirectWithError(string path, string error, string? returnUrl = null)
    {
        var query = $"?error={Uri.EscapeDataString(error)}";
        if (!string.IsNullOrEmpty(returnUrl)) query += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
        return Results.LocalRedirect(path + query);
    }

    private static string SafeReturnUrl(string value) =>
        value.StartsWith('/') && !value.StartsWith("//") ? value : "/";
    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
    private static bool IsValidEmail(string email) =>
        email.Length <= 254 && System.Net.Mail.MailAddress.TryCreate(email, out var address) && address.Address == email;
}
