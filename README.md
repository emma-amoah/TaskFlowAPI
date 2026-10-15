# TaskFlow API

A task management REST API built with ASP.NET Core, SQL Server, Entity Framework Core and ASP.NET Core Identity. I built it as a training project during my attachment at Pandora Software Consulting.

Users can register and log in, create projects, add tasks to them, assign the tasks to people, comment on tasks and attach files. Access is controlled with JWT tokens, roles and a policy.

**Author:** Emmanuel Kwasi Amoah

---

## What it uses

- ASP.NET Core Web API (.NET 10) and C#
- SQL Server with Entity Framework Core (migrations)
- ASP.NET Core Identity for users, roles and password hashing
- JWT bearer tokens plus refresh tokens
- AutoMapper for DTO mapping
- Swagger UI (OpenAPI) for testing the endpoints
- Git for source control

---

## How to run it

### 1. What you need first

- .NET 10 SDK
- SQL Server (I used a local instance, `Server=.`)
- Visual Studio 2022 or later, or the `dotnet` CLI
- The EF Core tool: `dotnet tool install --global dotnet-ef`

### 2. Clone the project

```
git clone <repository-url>
cd TaskFlowAPI/TaskFlowAPI
```

### 3. Set up the configuration

No secrets are stored in the repository. `appsettings.json` only has empty placeholders. The real values go in `appsettings.Development.json` (git-ignored) and in user-secrets.

Create `appsettings.Development.json` in the `TaskFlowAPI` project folder:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.;Database=TaskFlowDB;Trusted_Connection=True;TrustServerCertificate=true;MultipleActiveResultSets=true"
  },
  "JwtSettings": {
    "Secret": "<a long random string, at least 32 characters>",
    "Issuer": "https://localhost:7158",
    "Audience": "https://localhost:7158",
    "ExpiryInMinutes": 240
  }
}
```

Set the password for the seeded admin account with user-secrets:

```
dotnet user-secrets set "SeedData:AdminPassword" "<a strong password>"
```

The password needs at least 8 characters with an uppercase letter, a lowercase letter and a digit.

### 4. Create the database

```
dotnet ef database update
```

(In Visual Studio you can run `Update-Database` in the Package Manager Console.) This builds all the tables and also creates the three stored procedures, because they are part of the `AddStoredProcedures` migration.

### 5. Start the API

```
dotnet run --launch-profile https
```

The first start creates the three roles (Admin, Manager, User) and the admin user (`admin@taskflow.com`). Open **https://localhost:7158** and Swagger UI will load.

### 6. Add the demo data (optional)

I do not seed data from C# code. Demo data is a plain SQL script, `SeedDemoData.sql`. Run it in SQL Server Management Studio (or Visual Studio's SQL Server Object Explorer) against `TaskFlowDB` after the first start. It creates:

- Three demo users: `manager@taskflow.com` (Manager), `ama@taskflow.com` and `kofi@taskflow.com` (User). They use the **same password as the admin**.
- Five categories
- Three projects with members, tasks and comments. One of them, *Archive Demo Project*, is for demonstrating the transaction.

The script can be run more than once without creating duplicates.

---

## Using Swagger with a token

1. Call `POST /api/Auth/login` with an email and password.
2. Copy the `accessToken` from the response.
3. Click **Authorize** at the top of the page and paste the token. Paste only the token, not the word `Bearer`.
4. Now the protected endpoints will work.

The access token lasts 240 minutes in development. If you start getting 401 errors, log in again and re-authorize.

---

## Roles and permissions

| Role | What it can do |
|------|----------------|
| **Admin** | Everything, including purging old refresh tokens and viewing reports |
| **Manager** | Manage users, view reports, archive projects, edit or delete any project, task or comment |
| **User** | Create and view things, and edit or delete only the records they created |

- **Role-based rules:** the reports and the archive endpoint use `[Authorize(Roles = ...)]`.
- **Policy-based rule:** `RequireManagementLevel` (Admin or Manager) protects the whole `UsersController`.
- **Ownership rule:** for update and delete on tasks, comments, attachments and projects, the request is allowed only if the user is an Admin, a Manager, or the person who created the record. The creator is filled in by the server from the logged-in user. The client can never set it.

---

## Main endpoints

| Area | Endpoints |
|------|-----------|
| **Auth** | `POST register`, `login`, `refresh`, `logout`, `change-password`, `forgot-password`, `reset-password`, `confirm-email` |
| **Users** | `GET /api/Users`, `GET /api/Users/{id}`, `PUT /api/Users/{id}/toggle-status` |
| **Projects** | `GET` (list and by id), `POST`, `PUT /{id}`, `DELETE /{id}`, `POST /{id}/archive-and-cleanup` |
| **Tasks** | `GET` (list and by id), `POST`, `PUT /{id}`, `DELETE /{id}` |
| **Categories** | `GET` (list and by id), `POST`, `PUT /{id}`, `DELETE /{id}` |
| **Comments** | `GET /task/{taskId}`, `POST`, `DELETE /{id}` |
| **Attachments** | `POST /upload/{taskId}`, `GET /download/{id}`, `DELETE /{id}` |
| **Reports** | `GET /user-productivity/{userId}`, `GET /project-burndown/{projectId}`, `POST /purge-tokens` |

The list endpoints support `pageNumber`, `pageSize` (maximum 100), `searchTerm`, filters, and `sortBy` / `isDescending`. Only approved sort fields are accepted, so a client cannot send arbitrary column names.

---

## Design decisions

### Entity Framework Core and stored procedures

I used EF Core for normal create, read, update and delete work, and stored procedures only for reports. EF Core code is in C#, so it is easy to test and change. Stored procedures are better when a report has to count or group many rows, because the database does it in one trip and sends back a small result.

| Procedure | What it does | Why a stored procedure |
|-----------|--------------|------------------------|
| `sp_GetUserProductivityReport` | Counts a user's completed, active and overdue tasks | Three counts in one pass over the user's tasks. One row comes back and no task objects are loaded into memory. |
| `sp_GetProjectBurndownStatus` | Counts the tasks in each status for a project | Grouping happens in the database and only a few rows come back. |
| `sp_PurgeExpiredRefreshTokens` | Deletes expired or revoked refresh tokens | One set-based `DELETE` is cheaper than loading every old token into C# and removing them one by one. |

The procedures are created by a migration, so they are version-controlled with the project. They are called with `SqlParameter`, so user input is never joined into the SQL text. This protects against SQL injection.

### Transaction

`POST /api/Projects/{id}/archive-and-cleanup` does three things: it marks the project as archived, marks its open tasks as Completed, and removes the project's member links. All three run inside one transaction. If any step fails, everything is rolled back, so the project is never left half-archived. Calling it a second time on the same project returns 400.

### Concurrency (409 Conflict)

Every table has a `RowVersion` column that SQL Server changes on each update. When a client gets a task, it also gets the task's `rowVersion`. It must send that value back when it updates the task. If someone else changed the task in between, the versions no longer match and the API returns **409 Conflict** instead of overwriting the other person's change.

### Soft delete and audit fields

Deleting a project, task, comment or category only marks the row as deleted. A global query filter hides deleted rows from every query. Deleting a project also soft-deletes its tasks and their comments. Every record keeps `CreatedAt`, `CreatedBy`, `UpdatedAt` and `UpdatedBy`, filled in by the server from the logged-in user.

### Refresh tokens

Access tokens are short-lived. Refresh tokens are stored as hashes (never as plain text) and rotate each time they are used. If an old refresh token is used again, all of that user's sessions are revoked.

### File uploads

Files are limited to 5 MB and to an approved list of extensions. They are saved under a random GUID name. The original filename is never used for storage.

---

## HTTP status codes used

| Code | Meaning in this API |
|------|---------------------|
| 200 | Data returned (GET, login, reports) |
| 201 | A new record was created |
| 204 | Done, nothing to return (updates and deletes) |
| 400 | Invalid input or a broken business rule |
| 401 | No token, or the token is invalid or expired |
| 403 | Logged in, but not allowed (wrong role or not the owner) |
| 404 | The record does not exist |
| 409 | Concurrency conflict |
| 423 | Account locked after 5 failed logins |
| 500 | Unexpected server error (the middleware returns a clean JSON message) |

---

## Security notes

- Passwords are hashed by ASP.NET Core Identity. I did not write my own hashing.
- Accounts lock for 15 minutes after 5 failed logins.
- Secrets (JWT secret, admin password) are kept out of the repository.
- CORS only allows the local frontend origins `http://localhost:3000` and `http://localhost:5173`.
- Exception details are hidden from clients outside Development.

---

## Known gaps and next steps

This is a training project, so I built it in stages. Here is what is still missing:

- **Tests:** there is no test project yet. Next I will add unit tests for authentication and authorization, then integration tests.
- **Health check:** there is no health endpoint yet.
- **API versioning:** routes are `/api/[controller]`, not `/api/v1/...`.
- **Projects:** there are no endpoints for adding or removing members, and no project summary endpoint (the burndown report covers part of it).
- **Tasks:** status changes and reassignment go through `PUT`. Separate endpoints still need to be added.
- **Comments:** adding, listing and deleting work. Editing is not done.
- **Attachments:** the content type is saved but not validated.
- **Error handling:** there are no custom exception classes and no correlation id yet.
- **Email:** emails are mocked. The confirmation token is printed to the console instead of being sent. It should not be printed in a real system.
- **Documents:** the ERD and the short design document are still to be added.
