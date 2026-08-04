using MedicalCollege.Domain.Interfaces;
using MedicalCollege.Web.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MedicalCollege.Web.Filters;

/// <summary>Blocks portal access until MustChangePassword users update their password.</summary>
public class RequirePasswordChangeFilter : IAsyncActionFilter
{
    private readonly IUserRepository _users;

    public RequirePasswordChangeFilter(IUserRepository users) => _users = users;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            await next();
            return;
        }

        var controller = context.RouteData.Values["controller"]?.ToString() ?? "";
        var action = context.RouteData.Values["action"]?.ToString() ?? "";
        if (controller.Equals("Account", StringComparison.OrdinalIgnoreCase) &&
            (action.Equals("ChangePassword", StringComparison.OrdinalIgnoreCase)
             || action.Equals("Logout", StringComparison.OrdinalIgnoreCase)))
        {
            await next();
            return;
        }

        var userId = user.GetUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            await next();
            return;
        }

        var dbUser = await _users.GetByIdAsync(userId);
        if (dbUser?.MustChangePassword == true)
        {
            context.Result = new RedirectToActionResult("ChangePassword", "Account", null);
            return;
        }

        await next();
    }
}
