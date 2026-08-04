using MedicalCollege.Application.Interfaces;
using MedicalCollege.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalCollege.Web.Controllers;

[Authorize]
public class NotificationsController : Controller
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications) => _notifications = notifications;

    [HttpGet]
    public async Task<IActionResult> UnreadCount()
    {
        var count = await _notifications.GetUnreadCountAsync(User.GetUserId());
        return Json(new { count });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        await _notifications.MarkAllReadAsync(User.GetUserId());
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> Latest()
    {
        var items = (await _notifications.GetForUserAsync(User.GetUserId())).Take(8);
        return Json(items);
    }
}
