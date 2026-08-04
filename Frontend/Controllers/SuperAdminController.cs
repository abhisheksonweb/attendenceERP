using MedicalCollege.Application.Interfaces;
using MedicalCollege.Application.ViewModels;
using MedicalCollege.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalCollege.Web.Controllers;

[Authorize(Roles = "SuperAdmin")]
public class SuperAdminController : Controller
{
    private readonly IDashboardService _dashboard;
    private readonly IAdminService _admins;
    private readonly INotificationService _notifications;

    public SuperAdminController(IDashboardService dashboard, IAdminService admins, INotificationService notifications)
    {
        _dashboard = dashboard;
        _admins = admins;
        _notifications = notifications;
    }

    public async Task<IActionResult> Index()
    {
        var stats = await _dashboard.GetSuperAdminDashboardAsync();
        ViewBag.Notifications = await _notifications.GetForUserAsync(User.GetUserId());
        return View(stats);
    }

    public async Task<IActionResult> Admins()
    {
        var list = await _admins.GetAdminsAsync();
        return View(list);
    }

    [HttpGet]
    public IActionResult CreateAdmin() => View(new AdminFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAdmin(AdminFormViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await _admins.CreateAdminAsync(model, User.GetUserId(), User.GetDisplayName());
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "Unable to create admin.");
            return View(model);
        }
        TempData["Success"] = "Admin created successfully.";
        return RedirectToAction(nameof(Admins));
    }

    [HttpGet]
    public async Task<IActionResult> EditAdmin(string id)
    {
        var admin = await _admins.GetByIdAsync(id);
        if (admin is null) return NotFound();
        return View(admin);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAdmin(AdminFormViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await _admins.UpdateAdminAsync(model, User.GetUserId(), User.GetDisplayName());
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "Unable to update admin.");
            return View(model);
        }
        TempData["Success"] = "Admin updated.";
        return RedirectToAction(nameof(Admins));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DisableAdmin(string id)
    {
        var result = await _admins.DisableAdminAsync(id, User.GetUserId(), User.GetDisplayName());
        TempData[result.Success ? "Success" : "Error"] = result.Message ?? (result.Success ? "Admin disabled." : "Failed.");
        return RedirectToAction(nameof(Admins));
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(string id)
    {
        var admin = await _admins.GetByIdAsync(id);
        if (admin is null) return NotFound();
        return View(new ResetPasswordViewModel { UserId = id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await _admins.ResetPasswordAsync(model.UserId, model.NewPassword, User.GetUserId(), User.GetDisplayName());
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "Reset failed.");
            return View(model);
        }
        TempData["Success"] = "Password reset successfully.";
        return RedirectToAction(nameof(Admins));
    }

    public IActionResult ManageSystem() => View();
    public IActionResult ManageRoles() => View();
}
