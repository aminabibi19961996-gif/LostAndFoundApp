using Microsoft.AspNetCore.Identity;
using LostAndFoundApp.Models;

namespace LostAndFoundApp.Middleware
{
    /// <summary>
    /// Middleware that intercepts requests from authenticated local users who have MustChangePassword=true.
    /// Redirects them to the Change Password page and blocks access to all other pages until password is changed.
    /// AD-synced users are exempt from this check.
    /// </summary>
    public class MustChangePasswordMiddleware
    {
        private readonly RequestDelegate _next;

        public MustChangePasswordMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                // Allow access to change-password, logout, and static files without redirect loop
                var path = context.Request.Path.Value?.ToLower() ?? "";
                var allowedPaths = new[]
                {
                    "/account/changepassword",
                    "/account/logout",
                    "/account/login",
                    "/account/accessdenied",
                    "/home/error",
                    "/lib/",
                    "/css/",
                    "/js/",
                    "/images/",
                    "/favicon",
                    "/_framework/",
                    "/lostfounditem/photo/",
                    "/lostfounditem/attachment/"
                };

                bool isAllowed = allowedPaths.Any(p => path.StartsWith(p));

                if (!isAllowed)
                {
                    var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                    var user = await userManager.GetUserAsync(context.User);

                    if (user != null && !user.IsAdUser && user.MustChangePassword)
                    {
                        // For AJAX/API requests, return 401 JSON instead of redirect
                        // to prevent broken HTML responses in JavaScript
                        if (IsAjaxRequest(context.Request))
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            context.Response.ContentType = "application/json";
                            await context.Response.WriteAsync(
                                "{\"success\":false,\"message\":\"You must change your password before continuing.\",\"redirect\":\"/Account/ChangePassword\"}");
                            return;
                        }

                        context.Response.Redirect("/Account/ChangePassword");
                        return;
                    }
                }
            }

            await _next(context);
        }

        /// <summary>
        /// Detects AJAX requests by checking the X-Requested-With header or Accept header.
        /// </summary>
        private static bool IsAjaxRequest(HttpRequest request)
        {
            // jQuery/standard AJAX sets this header
            if (request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return true;

            // Fetch API or explicit JSON requests
            var accept = request.Headers["Accept"].ToString();
            if (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                return true;

            // Content-Type is JSON (POST with JSON body)
            var contentType = request.ContentType ?? "";
            if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
    }

    public static class MustChangePasswordMiddlewareExtensions
    {
        public static IApplicationBuilder UseMustChangePassword(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<MustChangePasswordMiddleware>();
        }
    }
}
