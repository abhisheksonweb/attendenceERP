using MedicalCollege.Application.Interfaces;
using MedicalCollege.Application.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace MedicalCollege.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StudentsController : ControllerBase
{
    private readonly IStudentService _students;

    public StudentsController(IStudentService students) => _students = students;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] StudentListFilter filter)
        => Ok(await _students.SearchAsync(filter));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var student = await _students.GetByIdAsync(id);
        return student is null ? NotFound() : Ok(student);
    }
}
