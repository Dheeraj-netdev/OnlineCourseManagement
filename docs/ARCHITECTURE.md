# Architecture

The application is split into two independently runnable ASP.NET Core 8 Web APIs.

```text
                 +-------------------+
                 |   Client / Tester |
                 +---------+---------+
                           |
             +-------------+-------------+
             |                           |
             v                           v
   +-------------------+       +-------------------+
   |    User Service   |<------|   Course Service  |
   |      :5101        | HTTP  |      :5102        |
   +---------+---------+       +---------+---------+
             |                           |
             v                           v
       EF Core InMemory             EF Core InMemory
          User store                  Course store
```

## Service responsibilities

### User Service

Owns registration, authentication, password hashing, user profiles, roles, and JWT issuance. It exposes a small internal user lookup endpoint for Course Service.

### Course Service

Owns courses and enrollments. It validates JWTs locally and uses the authenticated user's ID to enforce enrolled-only reads. Instructor operations are protected with role-based authorization.

Course Service calls User Service when it needs to verify an instructor or student. The two services have separate data stores and no shared entity or database project.

## Authentication and authorization

- Passwords are hashed with ASP.NET Core's `PasswordHasher`.
- JWTs contain the user ID, role, name, and email.
- Course Service validates the token signature, issuer, audience, algorithm, and expiration.
- Students can view and self-enroll.
- Instructors can create, update, delete, and enroll students.
- Course GET operations only return courses for which the authenticated user has an enrollment.

The development configuration contains sample local signing/service keys so the project can run without additional infrastructure. Production deployments should use secret management and a stronger identity/service-to-service authentication mechanism.

## Data access

Entity Framework Core's in-memory provider is used because it is part of the assignment requirements. Each service has its own DbContext and in-memory store. Data is lost when the corresponding process stops.

Database operations use async EF APIs and request cancellation. In-memory duplicate checks and course mutations are synchronized within each service process to keep the local POC state consistent.

For a production database, the main changes would be relational constraints, unique indexes, transactions, concurrency handling, and integration tests against the selected database engine.

## API and error handling

DTOs keep the HTTP contract separate from persisted entities. Course Service stores the instructor ID plus the instructor name at course creation/update time, avoiding a user-service call for every course read.

Service-to-service failures are distinguished from a valid "user not found" response. Dependency timeouts, authentication failures, or malformed responses return `503`.

## Testing

The test project covers the real ASP.NET Core request pipeline, including:

- registration and login
- JWT validation and role authorization
- course validation and CRUD
- enrollment rules and isolation
- search and pagination
- User Service dependency behavior
- concurrency cases
- generated OpenAPI security requirements

`tools/SmokeTest` runs the two services as real HTTP processes and verifies the main end-to-end workflow.
