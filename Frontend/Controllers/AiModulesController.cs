using MedicalCollege.Application.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalCollege.Web.Controllers;

[Authorize]
public class AiModulesController : Controller
{
    private ComingSoonViewModel Page(string title, string description, string icon)
        => new() { Title = title, Description = description, Icon = icon };

    public IActionResult FaceEnrollment()
    {
        if (User.IsInRole("Student"))
            return RedirectToAction("FaceEnrollment", "Student");

        return View("ComingSoon", Page(
            "Face Enrollment",
            "Students enroll their own faces from the Student portal. Open a class to sync roster to FRModule.",
            "bi-person-bounding-box"));
    }

    public IActionResult LiveCamera() =>
        View("ComingSoon", Page("Live Camera", "Live classroom camera streams will connect through FRModule recognition next.", "bi-camera-video"));

    public IActionResult AiRecognition() =>
        View("ComingSoon", Page("Face Recognition", "Automated attendance marking using face recognition REST APIs will plug in here.", "bi-cpu"));

    public IActionResult UnknownFaces() =>
        View("ComingSoon", Page("Unknown Faces", "Unrecognized detections will be reviewed by admins once the face recognition pipeline is connected.", "bi-question-circle"));

    public IActionResult CameraManagement() =>
        View("ComingSoon", Page("Camera Management", "Register and monitor campus cameras. Backend extension point ready for FastAPI.", "bi-webcam"));

    public IActionResult LivenessDetection() =>
        View("ComingSoon", Page("Liveness Detection", "Anti-spoofing / liveness checks will be provided by a future recognition microservice.", "bi-shield-check"));
}
