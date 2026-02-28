using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using LostAndFoundApp.Models;
using LostAndFoundApp.Services;
using LostAndFoundApp.ViewModels;

namespace LostAndFoundApp.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly AdSyncService _adSyncService;
        private readonly AdLoginRateLimiter _adLoginRateLimiter;
        private readonly ActivityLogService _activityLogService;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            AdSyncService adSyncService,
            AdLoginRateLimiter adLoginRateLimiter,
            ActivityLogService activityLogService,
            ILogger<AccountController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _adSyncService = adSyncService;
            _adLoginRateLimiter = adLoginRateLimiter;
            _activityLogService = activityLogService;
            _logger = logger;
        }

        // GET: /Account/Login
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Home");

            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        // =====================================================================
        // USERNAME RECOVERY — Forgot Username
        // =====================================================================

        // GET: /Account/ForgotUsername
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ForgotUsername()
        {
            return View(new ForgotUsernameViewModel());
        }

        // POST: /Account/ForgotUsername
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotUsername(ForgotUsernameViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Find user by email
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                // Don't reveal whether user exists
                TempData["SuccessMessage"] = "If an account with that email exists, the username has been sent.";
                return RedirectToAction("Login");
            }

            // In production, send email with username. For now, show on screen (demo).
            // TODO: Configure email service in production
            await _activityLogService.LogAsync(HttpContext, "Username Recovery",
                $"Username recovery requested for email '{model.Email}'.", "Auth");

            TempData["SuccessMessage"] = $"Your username is: <strong>{user.UserName}</strong>. Contact an administrator to have it sent to your email.";
            return RedirectToAction("Login");
        }

        // POST: /Account/Login
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.FindByNameAsync(model.UserName);

            if (user == null)
            {
                await _activityLogService.LogAsync(HttpContext, "Login Failed",
                    $"Login attempt for unknown user '{model.UserName}'.", "Auth", "Failed");
                ModelState.AddModelError(string.Empty, "Invalid username or password.");
                return View(model);
            }

            if (!user.IsActive)
            {
                await _activityLogService.LogAsync(HttpContext, "Login Blocked",
                    $"Login attempt by deactivated user '{model.UserName}'.", "Auth", "Failed");
                ModelState.AddModelError(string.Empty, "Your account has been deactivated. Contact an administrator.");
                return View(model);
            }

            if (user.IsAdUser)
            {
                // App-side rate limiting for AD login (Gap 4 fix)
                // Defense-in-depth since AD login bypasses Identity's lockout mechanism
                var (isLocked, remainingTime) = _adLoginRateLimiter.IsLockedOut(model.UserName);
                if (isLocked)
                {
                    await _activityLogService.LogAsync(HttpContext, "AD Login Blocked",
                        $"AD user '{model.UserName}' blocked by rate limiter. Lockout remaining: {remainingTime?.TotalMinutes:F0} min.", "Auth", "Failed");
                    _logger.LogWarning("AD login rate-limited for user '{User}'. Remaining: {Minutes:F0} min.", model.UserName, remainingTime?.TotalMinutes);
                    ModelState.AddModelError(string.Empty, "Too many failed login attempts. Please try again later.");
                    return View(model);
                }

                var adValid = _adSyncService.ValidateAdCredentials(model.UserName, model.Password);
                if (!adValid)
                {
                    _adLoginRateLimiter.RecordFailedAttempt(model.UserName);
                    var remaining = _adLoginRateLimiter.GetRemainingAttempts(model.UserName);
                    await _activityLogService.LogAsync(HttpContext, "AD Login Failed",
                        $"Active Directory authentication failed for user '{model.UserName}'. {remaining} attempt(s) remaining.", "Auth", "Failed");
                    ModelState.AddModelError(string.Empty, "Invalid Active Directory credentials.");
                    _logger.LogWarning("AD login failed for user '{User}'. {Remaining} attempts remaining.", model.UserName, remaining);
                    return View(model);
                }

                _adLoginRateLimiter.RecordSuccessfulLogin(model.UserName);
                await _signInManager.SignInAsync(user, model.RememberMe);
                await _activityLogService.LogAsync(HttpContext, "AD Login",
                    $"AD user '{model.UserName}' logged in successfully.", "Auth");
                _logger.LogInformation("AD user '{User}' logged in successfully.", model.UserName);
                return RedirectToLocal(model.ReturnUrl);
            }
            else
            {
                // Local user: standard Identity credential validation with lockout
                var result = await _signInManager.PasswordSignInAsync(
                    model.UserName, model.Password, model.RememberMe, lockoutOnFailure: true);

                if (result.Succeeded)
                {
                    await _activityLogService.LogAsync(HttpContext, "Login",
                        $"Local user '{model.UserName}' logged in successfully.", "Auth");
                    _logger.LogInformation("Local user '{User}' logged in successfully.", model.UserName);
                    return RedirectToLocal(model.ReturnUrl);
                }

                if (result.IsLockedOut)
                {
                    await _activityLogService.LogAsync(HttpContext, "Account Locked",
                        $"User '{model.UserName}' account locked out due to too many failed attempts.", "Auth", "Failed");
                    _logger.LogWarning("User '{User}' account locked out.", model.UserName);
                    ModelState.AddModelError(string.Empty, "Account locked out due to too many failed attempts. Try again later.");
                    return View(model);
                }

                await _activityLogService.LogAsync(HttpContext, "Login Failed",
                    $"Invalid password for user '{model.UserName}'.", "Auth", "Failed");
                ModelState.AddModelError(string.Empty, "Invalid username or password.");
                return View(model);
            }
        }

        // GET: /Account/ChangePassword
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> ChangePassword()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login");

            if (user.IsAdUser)
            {
                TempData["ErrorMessage"] = "Active Directory users must change their password through their organization's password management system.";
                return RedirectToAction("Index", "Home");
            }

            return View(new ChangePasswordViewModel());
        }

        // POST: /Account/ChangePassword
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return RedirectToAction("Login");

            if (user.IsAdUser)
            {
                TempData["ErrorMessage"] = "Active Directory users cannot change passwords here.";
                return RedirectToAction("Index", "Home");
            }

            var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                await _activityLogService.LogAsync(HttpContext, "Change Password Failed",
                    $"User '{user.UserName}' failed to change password.", "Auth", "Failed");
                return View(model);
            }

            user.MustChangePassword = false;
            await _userManager.UpdateAsync(user);
            await _signInManager.RefreshSignInAsync(user);

            await _activityLogService.LogAsync(HttpContext, "Change Password",
                $"User '{user.UserName}' changed their password successfully.", "Auth");
            _logger.LogInformation("User '{User}' changed their password.", user.UserName);
            TempData["SuccessMessage"] = "Your password has been changed successfully.";
            return RedirectToAction("Index", "Home");
        }

        // POST: /Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            var username = User.Identity?.Name;
            await _activityLogService.LogAsync(HttpContext, "Logout",
                $"User '{username}' logged out.", "Auth");
            await _signInManager.SignOutAsync();
            _logger.LogInformation("User logged out.");
            return RedirectToAction("Login");
        }

        // =====================================================================
        // SELF-SERVICE PROFILE — Users can update their own display name and email
        // =====================================================================

        // GET: /Account/Profile
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Profile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");

            var vm = new ProfileViewModel
            {
                UserName = user.UserName ?? "",
                DisplayName = user.DisplayName ?? "",
                Email = user.Email ?? "",
                IsAdUser = user.IsAdUser
            };

            return View(vm);
        }

        // POST: /Account/Profile
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(ProfileViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login");

            // Display name can be updated by all users
            user.DisplayName = model.DisplayName;

            // Email can be updated by local users only (AD users have email managed by AD)
            if (!user.IsAdUser)
            {
                user.Email = model.Email;
            }

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return View(model);
            }

            await _activityLogService.LogAsync(HttpContext, "Update Profile",
                $"User '{user.UserName}' updated their profile.", "Auth");
            _logger.LogInformation("User '{User}' updated their profile.", user.UserName);
            TempData["SuccessMessage"] = "Your profile has been updated successfully.";
            return RedirectToAction("Index", "Home");
        }

        // GET: /Account/AccessDenied
        [HttpGet]
        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            Response.StatusCode = 403;
            return View();
        }

        private IActionResult RedirectToLocal(string? returnUrl)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Index", "Home");
        }
    }
}
