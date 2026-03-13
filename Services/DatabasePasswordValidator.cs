using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using LostAndFoundApp.Data;
using LostAndFoundApp.Models;

namespace LostAndFoundApp.Services
{
    /// <summary>
    /// Custom password validator that reads password policy from the database.
    /// Replaces the hardcoded Identity password options with dynamic, SuperAdmin-configurable policy.
    /// Falls back to sensible defaults if no policy exists in the database.
    /// </summary>
    public class DatabasePasswordValidator : IPasswordValidator<ApplicationUser>
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public DatabasePasswordValidator(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "PasswordRequired",
                    Description = "Password is required."
                });
            }

            // Read the current policy from the database
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var policy = await context.PasswordPolicySettings.FirstOrDefaultAsync();

            // Defaults if no policy saved yet
            var minLength = policy?.MinimumLength ?? 8;
            var requireDigit = policy?.RequireDigit ?? true;
            var requireLower = policy?.RequireLowercase ?? true;
            var requireUpper = policy?.RequireUppercase ?? true;
            var requireSpecial = policy?.RequireNonAlphanumeric ?? true;

            var errors = new List<IdentityError>();

            if (password.Length < minLength)
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordTooShort",
                    Description = $"Password must be at least {minLength} characters long."
                });
            }

            if (requireDigit && !password.Any(char.IsDigit))
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordRequiresDigit",
                    Description = "Password must contain at least one digit ('0'-'9')."
                });
            }

            if (requireLower && !password.Any(char.IsLower))
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordRequiresLower",
                    Description = "Password must contain at least one lowercase letter ('a'-'z')."
                });
            }

            if (requireUpper && !password.Any(char.IsUpper))
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordRequiresUpper",
                    Description = "Password must contain at least one uppercase letter ('A'-'Z')."
                });
            }

            if (requireSpecial && password.All(c => char.IsLetterOrDigit(c)))
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordRequiresNonAlphanumeric",
                    Description = "Password must contain at least one special character."
                });
            }

            return errors.Count > 0 ? IdentityResult.Failed(errors.ToArray()) : IdentityResult.Success;
        }
    }
}
