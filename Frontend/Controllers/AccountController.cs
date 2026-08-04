using System.Security.Claims;
using MedicalCollege.Application.Interfaces;
using MedicalCollege.Application.ViewModels;
using MedicalCollege.Domain.Enums;
using MedicalCollege.Web.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalCollege.Web.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly IParentNotificationService _parents;

    public AccountController(IAuthService authService, IParentNotificationService parents)
    {
        _authService = authService;
        _parents = parents;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToDashboard(User.GetRole());

        ViewBag.ReturnUrl = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _authService.ValidateLoginAsync(model.Email, model.Password);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View(model);
        }

        if (user.Role == UserRole.SuperAdmin)
        {
            ModelState.AddModelError(string.Empty, "Super Admin access has been removed from this portal.");
            return View(model);
        }

        if (model.PreferredRole.HasValue && user.Role != model.PreferredRole.Value)
        {
            ModelState.AddModelError(string.Empty,
                $"This account is a {user.Role} account. Clear the role filter or choose {user.Role}.");
            return View(model);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.GivenName, user.FullName),
            new(ClaimTypes.Role, user.Role.ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var props = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(14) : DateTimeOffset.UtcNow.AddHours(8)
        };

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, props);
        HttpContext.Session.SetString("UserFullName", user.FullName);
        HttpContext.Session.SetString("UserRole", user.Role.ToString());

        if (user.MustChangePassword)
            return RedirectToAction(nameof(ChangePassword));

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToDashboard(user.Role.ToString());
    }

    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await _authService.ChangePasswordAsync(User.GetUserId(), model);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "Unable to change password.");
            return View(model);
        }

        TempData["Success"] = "Password changed successfully.";
        return RedirectToDashboard(User.GetRole());
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public IActionResult ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        TempData["Success"] = "If an account exists for that email, password reset instructions have been sent. (Prototype UI only)";
        return RedirectToAction(nameof(Login));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    /// <summary>Parent unsubscribe link from alert emails.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> UnsubscribeParentAlerts(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            ViewBag.Message = "Invalid unsubscribe link.";
            ViewBag.Ok = false;
            return View("UnsubscribeParentAlerts");
        }

        var result = await _parents.UnsubscribeParentEmailAsync(token.Trim());
        ViewBag.Ok = result.Success;
        ViewBag.Message = result.Message ?? (result.Success
            ? "You have been unsubscribed from parent attendance emails."
            : "Unable to unsubscribe.");
        return View("UnsubscribeParentAlerts");
    }

    private IActionResult RedirectToDashboard(string role) => role switch
    {
        nameof(UserRole.Admin) => RedirectToAction("Index", "Admin"),
        nameof(UserRole.Student) => RedirectToAction("Index", "Student"),
        _ => RedirectToAction(nameof(Login))
    };
}
