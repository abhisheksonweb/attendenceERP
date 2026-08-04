using System.Globalization;
using System.Text;
using MedicalCollege.Application.Common;
using MedicalCollege.Application.ViewModels;
using MedicalCollege.Web.Helpers;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MedicalCollege.Web.Services;

public static class ReportExportBuilder
{
    static ReportExportBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] BuildCsv(IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static byte[] AttendanceCsv(IEnumerable<AttendanceRecordViewModel> records, AttendanceStats? stats)
    {
        var rows = new List<string[]>
        {
            new[] { "Attendance Report", DateTime.Today.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) },
            new[] { "Present", (stats?.PresentCount ?? 0).ToString(CultureInfo.InvariantCulture) },
            new[] { "Absent", (stats?.AbsentCount ?? 0).ToString(CultureInfo.InvariantCulture) },
            new[] { "Rate %", (stats?.Percentage ?? 0).ToString("0.#", CultureInfo.InvariantCulture) },
            Array.Empty<string>(),
            new[] { "Student ID", "Student Name", "Department", "Course", "Status", "Source", "First In", "Last Out" }
        };

        rows.AddRange(records.Select(r => new[]
        {
            r.StudentCode,
            r.StudentName,
            r.Department,
            r.Course,
            AttendanceDisplay.StatusLabel(r.Status),
            r.Source,
            r.FirstIn ?? "",
            r.LastOut ?? ""
        }));

        return BuildCsv(rows);
    }

    public static byte[] StudentsCsv(IEnumerable<StudentFormViewModel> students)
    {
        var rows = new List<string[]>
        {
            new[] { "Student ID", "Name", "Email", "Department", "Course", "Semester", "Mobile", "Status" }
        };
        rows.AddRange(students.Select(s => new[]
        {
            s.StudentId,
            s.Name,
            s.Email,
            s.Department,
            s.Course,
            s.Semester,
            s.Mobile,
            s.IsActive ? "Active" : "Inactive"
        }));
        return BuildCsv(rows);
    }

    public static byte[] ChartCsv(string labelHeader, IEnumerable<ChartPoint> points)
    {
        var rows = new List<string[]> { new[] { labelHeader, "Students" } };
        rows.AddRange(points.OrderByDescending(p => p.Value)
            .Select(p => new[] { p.Label, p.Value.ToString(CultureInfo.InvariantCulture) }));
        return BuildCsv(rows);
    }

    public static byte[] AttendancePdf(IEnumerable<AttendanceRecordViewModel> records, AttendanceStats? stats)
    {
        var list = records.ToList();
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Header().Column(col =>
                {
                    col.Item().Text("Attendance Report").SemiBold().FontSize(18).FontColor(Colors.Blue.Medium);
                    col.Item().Text($"Date: {DateTime.Today:dd MMM yyyy}").FontSize(10).FontColor(Colors.Grey.Darken2);
                    if (stats is not null)
                    {
                        col.Item().PaddingTop(6).Text(
                                $"Present: {stats.PresentCount}   Absent: {stats.AbsentCount}   Rate: {stats.Percentage:0.#}%")
                            .FontSize(10);
                    }
                });

                page.Content().PaddingTop(16).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(2);
                        c.RelativeColumn(1.4f);
                        c.RelativeColumn(1.2f);
                        c.RelativeColumn(1);
                    });

                    table.Header(h =>
                    {
                        h.Cell().Background(Colors.Blue.Medium).Padding(6).Text("Student").SemiBold().FontColor(Colors.White).FontSize(9);
                        h.Cell().Background(Colors.Blue.Medium).Padding(6).Text("Department").SemiBold().FontColor(Colors.White).FontSize(9);
                        h.Cell().Background(Colors.Blue.Medium).Padding(6).Text("Status").SemiBold().FontColor(Colors.White).FontSize(9);
                        h.Cell().Background(Colors.Blue.Medium).Padding(6).Text("Source").SemiBold().FontColor(Colors.White).FontSize(9);
                    });

                    foreach (var r in list)
                    {
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text($"{r.StudentName}\n{r.StudentCode}").FontSize(9);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(r.Department).FontSize(9);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(AttendanceDisplay.StatusLabel(r.Status)).FontSize(9);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(r.Source).FontSize(9);
                    }

                    if (list.Count == 0)
                        table.Cell().ColumnSpan(4).Padding(8).Text("No records for today.").FontColor(Colors.Grey.Medium);
                });

                page.Footer().AlignCenter()
                    .Text($"Generated {DateTime.Now:dd MMM yyyy HH:mm}")
                    .FontSize(9).FontColor(Colors.Grey.Medium);
            });
        }).GeneratePdf();
    }

    public static byte[] StudentsPdf(IEnumerable<StudentFormViewModel> students)
    {
        var list = students.ToList();
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(36);
                page.Header().Column(col =>
                {
                    col.Item().Text("Student Report").SemiBold().FontSize(18).FontColor(Colors.Blue.Medium);
                    col.Item().Text($"{list.Count} students").FontSize(10).FontColor(Colors.Grey.Darken2);
                });

                page.Content().PaddingTop(14).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(1.2f);
                        c.RelativeColumn(2);
                        c.RelativeColumn(1.4f);
                        c.RelativeColumn(1.4f);
                        c.RelativeColumn(0.8f);
                        c.RelativeColumn(0.8f);
                    });

                    table.Header(h =>
                    {
                        h.Cell().Background(Colors.Blue.Medium).Padding(6).Text("Student ID").SemiBold().FontColor(Colors.White).FontSize(9);
                        h.Cell().Background(Colors.Blue.Medium).Padding(6).Text("Name").SemiBold().FontColor(Colors.White).FontSize(9);
                        h.Cell().Background(Colors.Blue.Medium).Padding(6).Text("Department").SemiBold().FontColor(Colors.White).FontSize(9);
                        h.Cell().Background(Colors.Blue.Medium).Padding(6).Text("Course").SemiBold().FontColor(Colors.White).FontSize(9);
                        h.Cell().Background(Colors.Blue.Medium).Padding(6).Text("Sem").SemiBold().FontColor(Colors.White).FontSize(9);
                        h.Cell().Background(Colors.Blue.Medium).Padding(6).Text("Status").SemiBold().FontColor(Colors.White).FontSize(9);
                    });

                    foreach (var s in list)
                    {
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(s.StudentId).FontSize(9);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(s.Name).FontSize(9);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(s.Department).FontSize(9);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(s.Course).FontSize(9);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(s.Semester).FontSize(9);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(s.IsActive ? "Active" : "Inactive").FontSize(9);
                    }

                    if (list.Count == 0)
                        table.Cell().ColumnSpan(6).Padding(8).Text("No students found.").FontColor(Colors.Grey.Medium);
                });

                page.Footer().AlignCenter()
                    .Text($"Generated {DateTime.Now:dd MMM yyyy HH:mm}")
                    .FontSize(9).FontColor(Colors.Grey.Medium);
            });
        }).GeneratePdf();
    }

    public static byte[] ChartPdf(string title, string labelHeader, IEnumerable<ChartPoint> points)
    {
        var list = points.OrderByDescending(p => p.Value).ToList();
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Header().Column(col =>
                {
                    col.Item().Text(title).SemiBold().FontSize(18).FontColor(Colors.Blue.Medium);
                    col.Item().Text($"Generated {DateTime.Now:dd MMM yyyy HH:mm}").FontSize(10).FontColor(Colors.Grey.Darken2);
                });

                page.Content().PaddingTop(16).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3);
                        c.RelativeColumn(1);
                    });

                    table.Header(h =>
                    {
                        h.Cell().Background(Colors.Blue.Medium).Padding(6).Text(labelHeader).SemiBold().FontColor(Colors.White).FontSize(9);
                        h.Cell().Background(Colors.Blue.Medium).Padding(6).Text("Students").SemiBold().FontColor(Colors.White).FontSize(9);
                    });

                    foreach (var p in list)
                    {
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(p.Label).FontSize(9);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(p.Value.ToString(CultureInfo.InvariantCulture)).FontSize(9);
                    }

                    if (list.Count == 0)
                        table.Cell().ColumnSpan(2).Padding(8).Text("No data.").FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();
    }

    private static string EscapeCsv(string? value)
    {
        value ??= "";
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
