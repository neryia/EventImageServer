# EventImageServer — Project Context (Min)

## Architecture
ASP.NET Core 7 Web API (C#), monolithic REST backend for an event-planning app. SQLite (`eventimage.db`, EF Core, `EnsureCreated` — no migrations) is the only datastore. Auth is stateless via Firebase-issued JWT bearer tokens (`https://securetoken.google.com/eventimage-72337`), validated by ASP.NET JWT middleware; no server-side sessions. Users are **auto-provisioned** on first authenticated request (no signup endpoint) as `RoleType.EventOwner`, keyed by Firebase UID. CORS is locked to `http://localhost:3000` (React dev client). A separate Python microservice (external, called over HTTP) performs seating optimization. Uploaded files are served as static files from `UploadedImages/` under `/UploadedImages`.

## Main Modules
- **Program.cs** — composition root: CORS, JWT auth, DbContext, static file serving, middleware pipeline (`UseRouting → UseCors → UseAuthentication → UseAuthorization → MapControllers`).
- **Contexts/AppDbContext.cs** — EF Core `DbContext`; owns all entity relationships/cascade rules.
- **Controllers/** — `ImagesController`, `SeatingController`, `VendorsController`, `BudgetController`: one controller per feature area, all `[Authorize]`.
- **Services/Sitting.cs** — thin HTTP client wrapper calling the external Python seating-arrangement service.
- **Models/** — EF entities + DTO-like request classes (mostly nested inside controllers, not in Models/).

## Public APIs
All routes prefixed by controller name (`[controller]`), all require `Authorize` (Firebase JWT), UID pulled from `user_id` claim.

**ImagesController** (`/Images`)
- `GET Gallery` — list current user's uploaded images.
- `POST Upload` — upload image/video to user's folder.
- `DELETE Delete`, `DELETE Gallery/{fileName}` — remove uploaded file.

**SeatingController** (`/Seating`) — guest/table seat-planning for EventOwners only.
- `GET /` — full seating state (`tables` + `guests` + `categories`) for owner.
- `POST Tables`, `PUT Tables/{id}`, `DELETE Tables/{id}` — table CRUD.
- `POST Guests`, `PUT Guests/{id}`, `DELETE Guests/{id}` — guest CRUD; auto-registers a `GuestCategory` row (default color) the first time a new `Category` string is used.
- `PUT Category/{categoryValue}/Color` — bulk-sets the display color for a category (upsert); affects all guests sharing that value since color lives on `GuestCategory`, not per-guest.
- `POST SaveArrangement` — atomically apply a full guest→table assignment set (transactional, pre-validated).
- `POST AutoAssign` — delegates to `Sitting.Arrange` (external Python service) to auto-seat guests; supports locking specific guests to their current table.

**VendorsController** (`/Vendors`) — vendor/timeline/attachment management for EventOwners.
- `GET /`, `GET summary`, `GET {id}` — list/summarize/fetch vendors.
- `POST /`, `PUT {id}`, `DELETE {id}` — vendor CRUD.
- `POST {id}/attachments` — attach files to a vendor (plus a timeline-related endpoint).

**BudgetController** (`/Budget`) — one budget per EventOwner (lazily created, `GET` never returns null).
- `GET /` — full budget: `{ totalBudget, categories[], expenses[] }`.
- `PUT /` — update `totalBudget`.
- `POST/PUT/DELETE categories/{id}` — `BudgetCategory` CRUD (`name`, `plannedAmount`, `linkedVendorCategory` int? mapping to `VendorCategory`).
- `POST/PUT/DELETE expenses/{id}` — `BudgetExpense` CRUD (`name`, `categoryId`, `amount`, `paidAmount`, `dueDate`, `vendorId?`); cascade-deleted with their category/budget.
- Vendor-vs-budget rollups (planned-vs-actual, cost-per-guest) are computed **client-side** from `GET /Vendors`; the server does no aggregation.

## Data Flow
1. Client (React) authenticates via Firebase, sends `Authorization: Bearer <JWT>` on every request.
2. Middleware validates JWT issuer/audience/lifetime → populates `User` claims (`user_id`, `email`, `name`).
3. Each controller calls `GetUID()` to read `user_id`, then (Seating/Vendors) `RequireEventOwner()` which fetches or **lazily creates** the `Users` row and enforces `RoleType.EventOwner`.
4. Controller reads/writes EF Core entities scoped by `OwnerId == userId` (per-user data isolation is enforced in application code, not DB-level security).
5. For seating auto-assignment: SeatingController builds a `SeatingArrangeRequest` (tables + guests DTOs) → `Sitting.Arrange()` POSTs JSON to external service at `http://localhost:8000/seating/arrange` → response (`assignments`, `unseated`, `score`) is mapped back onto `Guest.TableId` and persisted.
6. Image uploads are written to disk under `UploadedImages/{uid}/...` and served back via static file middleware at matching URL path.

## Important Conventions
- Every mutating/protected endpoint starts by resolving the caller via `GetUID()` + `RequireEventOwner()`; missing/invalid UID → 401, wrong role → 403. This pattern is duplicated per-controller (not shared via base class/middleware) — replicate it exactly when adding controllers.
- All EF entities carry `OwnerId` (Firebase UID string) for tenant isolation; always filter queries by `OwnerId == owner.Id`.
- Navigation properties that would create serialization cycles are marked `[JsonIgnore]` (e.g., `Guest.Table`, `Guest.Owner`).
- Request/response shapes for POST/PUT are defined as nested `public class ...Request` inside the controller, not separate files.
- Multi-step writes that must be all-or-nothing (e.g., `SaveArrangement`) use explicit EF `Database.BeginTransactionAsync()` with validate-then-apply-then-commit, and roll back on exception.
- No API versioning, no global error-handling middleware — each action has its own try/catch returning `StatusCode(500, ...)`.
- **No EF migrations** — schema changes rely on `Database.EnsureCreated()`, which only creates the DB on first run and does **not** alter an existing `eventimage.db`. When adding entities/columns: run `dotnet ef dbcontext script` to get the exact CREATE TABLE/INDEX SQL, then apply just the new statements directly via `sqlite3 eventimage.db` (wrapped in `IF NOT EXISTS`) to avoid wiping existing data.

## Key Files
- [Program.cs](Program.cs) — startup/config.
- [Contexts/AppDbContext.cs](Contexts/AppDbContext.cs) — schema & relationships.
- [Controllers/SeatingController.cs](Controllers/SeatingController.cs), [Controllers/VendorsController.cs](Controllers/VendorsController.cs), [Controllers/ImagesController.cs](Controllers/ImagesController.cs)
- [Services/Sitting.cs](Services/Sitting.cs) — external seating-service client + DTOs.
- [Models/Guest.cs](Models/Guest.cs), [Models/Table.cs](Models/Table.cs), [Models/Vendor.cs](Models/Vendor.cs), [Models/Users.cs](Models/Users.cs), [Models/GuestCategory.cs](Models/GuestCategory.cs), [Models/Budget.cs](Models/Budget.cs), [Models/BudgetCategory.cs](Models/BudgetCategory.cs), [Models/BudgetExpense.cs](Models/BudgetExpense.cs) — core entities.
- `appsettings.json` / `appsettings.Development.json` — config (currently minimal; Firebase project ID hardcoded in `Program.cs`, not config-driven).
