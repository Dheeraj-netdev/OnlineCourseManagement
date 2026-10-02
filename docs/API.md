# API Reference

Both services return JSON using camelCase properties. Protected endpoints use:

```text
Authorization: Bearer <accessToken>
```

Validation and business-rule errors use HTTP 400/404/409 responses as appropriate. Authentication failures return 401 and role restrictions return 403.

## User Service

Base URL: `http://localhost:5101`

| Method | Endpoint | Access | Description |
|---|---|---|---|
| POST | `/api/auth/register` | Public | Register a Student or Instructor |
| POST | `/api/auth/login` | Public | Authenticate and issue a JWT |
| GET | `/api/users/me` | JWT | Return the current user |
| GET | `/internal/users/{id}` | Service key | Lookup a user for Course Service |
| GET | `/health` | Public | Liveness check |

### Register

```json
{
  "name": "Ada Instructor",
  "email": "ada@example.test",
  "password": "ReviewPass2026!",
  "role": "Instructor"
}
```

`role` defaults to `Student`. Valid roles are `Student` and `Instructor`. Email matching is case-insensitive. Passwords are 10–128 characters and require uppercase, lowercase, and a digit. Passwords and password hashes are never returned.

### Login

```json
{
  "email": "ada@example.test",
  "password": "ReviewPass2026!"
}
```

A successful response contains `accessToken`, `tokenType`, `expiresAtUtc`, and the user's profile.

## Course Service

Base URL: `http://localhost:5102`

| Method | Endpoint | Access | Description |
|---|---|---|---|
| GET | `/api/courses` | JWT | List enrolled courses |
| GET | `/api/courses/search` | JWT | Search enrolled courses |
| GET | `/api/courses/{id}` | JWT | Read an enrolled course |
| POST | `/api/courses` | Instructor | Create a course |
| PUT | `/api/courses/{id}` | Instructor | Update a course |
| DELETE | `/api/courses/{id}` | Instructor | Delete a course |
| POST | `/api/courses/{id}/enrollments/me` | JWT | Enroll the current user |
| POST | `/api/courses/{id}/enrollments` | Instructor | Enroll a Student |
| GET | `/health` | Public | Liveness check |

### Create / update course

```json
{
  "title": "Distributed .NET",
  "description": "Build APIs with clear service boundaries.",
  "startDate": "2030-01-10T09:00:00Z",
  "endDate": "2030-01-12T09:00:00Z",
  "instructorId": "00000000-0000-0000-0000-000000000001"
}
```

Title and description are required. The end date must be later than the start date. The instructor must exist in User Service and have the `Instructor` role.

The course response contains:

```json
{
  "id": "00000000-0000-0000-0000-000000000002",
  "title": "Distributed .NET",
  "description": "Build APIs with clear service boundaries.",
  "startDate": "2030-01-10T09:00:00Z",
  "endDate": "2030-01-12T09:00:00Z",
  "instructorId": "00000000-0000-0000-0000-000000000001",
  "instructorName": "Ada Instructor"
}
```

Creating a course does not automatically enroll the creator.

## Enrollment

Self-enrollment uses the authenticated user's ID from the JWT and requires no request body:

```text
POST /api/courses/{id}/enrollments/me
```

Instructor enrollment accepts:

```json
{
  "studentId": "00000000-0000-0000-0000-000000000003"
}
```

Enrollment is idempotent. Students cannot enroll other users. Instructors can enroll Students.

## Search and pagination

Collection endpoints support:

- `pageNumber` – default `1`
- `pageSize` – default `10`, maximum `100`

`/api/courses/search` additionally accepts `startDate`, `endDate`, and `instructorName`.

Date filtering uses inclusive interval overlap. Instructor name matching is case-insensitive substring matching. Enrollment filtering is applied before search, counts, and pagination, so users cannot discover courses in which they are not enrolled.

Example:

```text
GET /api/courses/search?startDate=2030-01-11T00%3A00%3A00Z&endDate=2030-01-13T00%3A00%3A00Z&instructorName=ada&pageNumber=1&pageSize=10
```

Response:

```json
{
  "items": [],
  "pageNumber": 1,
  "pageSize": 10,
  "totalCount": 0,
  "totalPages": 0
}
```

## Service-to-service calls

Course Service calls User Service for instructor and student validation using the internal service key. A dependency failure is returned as `503` rather than being treated as a missing user.
