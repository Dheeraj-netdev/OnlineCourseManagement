# Online Course Management

Proof of Concept: Online Course Management System using .NET. The system consist sof two independently runnable micro services: Course Service for courses and enrollment, and User Service for registration, authentication, and JWT-based authorization.

- **User Service** – registration, login, JWT issuance, user profiles, and the internal user directory.
- **Course Service** – course CRUD, enrollment, search, and pagination.
- **Tests** – unit and HTTP integration coverage for authentication, authorization, course rules, and service-to-service behavior.

## Requirements

- .NET 8 SDK (8.0.300 or later in the .NET 8 feature band)
- Internet access for the first NuGet restore

No external database or infrastructure is required. Both services use EF Core's in-memory provider as required by the assignment.

## Run locally

From the `OnlineCourseManagement` directory:

### macOS / Linux

```bash
bash scripts/run-local.sh
```

### Windows

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-local.ps1
```

The services run on:

- User Service: `http://localhost:5101`
- Course Service: `http://localhost:5102`

Swagger UI is available at `/swagger` on each service. `requests.http` contains sample requests.

To stop the services, press `Ctrl+C`.

## Build and test

```bash
dotnet restore OnlineCourseManagement.sln --locked-mode
dotnet build OnlineCourseManagement.sln --configuration Release
dotnet test OnlineCourseManagement.sln --configuration Release
```

The repository also includes a small live smoke-test application under `tools/SmokeTest` for exercising both services over HTTP.

```bash
bash scripts/run-local.sh --smoke
```

## API overview

### User Service

- `POST /api/auth/register`
- `POST /api/auth/login`
- `GET /api/users/me`
- `GET /internal/users/{id}` – service-to-service lookup
- `GET /health`

### Course Service

- `GET /api/courses`
- `GET /api/courses/search`
- `GET /api/courses/{id}`
- `POST /api/courses`
- `PUT /api/courses/{id}`
- `DELETE /api/courses/{id}`
- `POST /api/courses/{id}/enrollments/me`
- `POST /api/courses/{id}/enrollments`
- `GET /health`

See [`docs/API.md`](docs/API.md) for request rules and authorization behavior. Generated OpenAPI documents are included under `docs/` for offline review.

## Project structure

```text
src/UserService/                  User and authentication API
src/CourseService/                Course and enrollment API
tests/OnlineCourseManagement.Tests/Unit and HTTP integration tests
tools/SmokeTest/                  Live end-to-end smoke test
scripts/                          Local launch scripts
docs/API.md                       API behavior and rules
docs/ARCHITECTURE.md              Architecture and implementation notes
```

The services have separate data stores and do not reference each other's projects. Course Service communicates with User Service through HTTP for instructor and student validation.

## Notes

- JWT bearer authentication is used between clients and Course Service.
- Instructor and Student roles are enforced through ASP.NET Core authorization.
- Course reads are limited to courses in which the authenticated user is enrolled.
- Course dates use UTC values in API responses.
- Data is in-memory and is discarded when the service process stops.
