# Medical College Attendance System

ASP.NET Core 8 portal with Clean Architecture, JSON storage, cookie auth, Razor UI, and a local face recognition module (FRModule).

## Structure

```
Backend/
  MedicalCollege.Domain/
  MedicalCollege.Application/
  MedicalCollege.Infrastructure/   # JSON repositories + FR sync
  MedicalCollege.Api/              # REST API
Frontend/                          # MVC Razor portal
  App_Data/                        # users.json, students.json, ...
FRModule/                          # Face recognition (Flask + camera)
```

## Prerequisites

Projects target **.NET 8** (`net8.0`). Install the [.NET 8 Runtime or SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to run locally.

## Run (Frontend MVC)

```bash
dotnet run --project Frontend/MedicalCollege.Web.csproj
```

Open `http://localhost:5148` (see `Frontend/Properties/launchSettings.json`).

## Run (API)

```bash
dotnet run --project Backend/MedicalCollege.Api/MedicalCollege.Api.csproj
```

Swagger UI is available in Development.

## Run (Face module)

```bash
cd FRModule
python app.py
```

Default: `http://127.0.0.1:8000`

## Demo logins

| Role | Email | Password |
|------|-------|----------|
| Admin | admin@medcollege.edu | Admin@123 |

Students are created by admin after creating a class (batch).

## Notes

- Face recognition runs locally via FRModule (on-premises).
- Storage is JSON under `Frontend/App_Data` (swap repositories later for SQL without changing controllers).
