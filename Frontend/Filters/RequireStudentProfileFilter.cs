using MedicalCollege.Application.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MedicalCollege.Web.Filters;

/// <summary>
/// Signs out and redirects when a student session has no matching profile (e.g. after data reset).
/// </summary>
public class RequireStudentProfileFilter : IAsyncActionFilter
{
    private readonly IStudentService _students;

    public RequireStudentProfileFilter(IStudentService students) => _students = students;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (!user.Identity?.IsAuthenticated ?? true)
        {
            await next();
            return;
        }

        var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? user.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            await next();
            return;
        }

        var student = await _students.GetByUserIdAsync(userId);
        if (student is not null)
        {
            await next();
            return;
        }

        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (context.Controller is Controller controller)
        {
            controller.TempData["Error"] =
                "Your student profile is no longer available. Sign in as admin or ask your admin to recreate your account.";
        }
        context.Result = new RedirectToActionResult("Login", "Account", null);
    }
}
