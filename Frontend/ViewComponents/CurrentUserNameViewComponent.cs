using MedicalCollege.Domain.Interfaces;
using MedicalCollege.Web.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace MedicalCollege.Web.ViewComponents;

public class CurrentUserNameViewComponent : ViewComponent
{
    private readonly IUserRepository _users;
    private readonly IStudentRepository _students;

    public CurrentUserNameViewComponent(IUserRepository users, IStudentRepository students)
    {
        _users = users;
        _students = students;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var principal = HttpContext.User;
        var userId = principal.GetUserId();
        if (string.IsNullOrEmpty(userId))
            return View("Default", principal.GetDisplayName());

        var user = await _users.GetByIdAsync(userId);
        if (user is null)
            return View("Default", principal.GetDisplayName());

        // Prefer live student name (may have been renamed) over login claim.
        var student = await _students.GetByUserIdAsync(userId);
        var name = !string.IsNullOrWhiteSpace(student?.Name)
            ? student!.Name
            : user.FullName;

        return View("Default", name);
    }
}
