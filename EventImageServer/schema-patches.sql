-- schema-patches.sql
--
-- This project has NO EF Core migrations: Database.EnsureCreated() only
-- creates eventimage.db the very first time it doesn't exist, and never
-- alters an existing database. Every time a new feature adds a column or
-- table to a Model, the matching DDL must be appended below (never edited
-- or removed once committed) and run by hand against the live database.
--
-- Workflow for each schema change:
--   1. Add/update the C# entity in Models/ (and DbSet in Contexts/AppDbContext.cs).
--   2. Get the exact statement EF would generate:
--        dotnet ef dbcontext script --project EventImageServer.csproj
--      (or hand-write it to match EF's SQLite column-naming conventions).
--   3. CREATE TABLE/INDEX statements below use IF NOT EXISTS and are safe to
--      re-run. ALTER TABLE ... ADD COLUMN is NOT safe to re-run (SQLite has
--      no "ADD COLUMN IF NOT EXISTS", and — verified against sqlite3 3.43 —
--      a `SELECT CASE ... THEN RAISE(IGNORE) END` guard does NOT work outside
--      a trigger; it errors with "RAISE() may only be used within a
--      trigger-program"). Before applying an ALTER TABLE statement, check
--      the column doesn't already exist:
--        sqlite3 eventimage.db "PRAGMA table_info('Guests');"
--      and only run that one ALTER TABLE line by hand if it's missing.
--   4. Apply CREATE TABLE/INDEX statements in bulk: sqlite3 eventimage.db < schema-patches.sql
--      (any ALTER TABLE lines already applied to this dev DB are commented
--      out below once run, so re-running the file stays safe).
--   5. Never reorder or delete earlier statements — this file is a log,
--      applied top-to-bottom.
--
-- Each CREATE TABLE/INDEX statement below MUST be idempotent (IF NOT EXISTS).

-- =========================================================================
-- (Phase 0 shipped no schema changes — SaveArrangement/AutoAssign fixes were
-- application-code only.)
-- =========================================================================

-- Phase 2.A — Task checklist (PlanningTasks)
CREATE TABLE IF NOT EXISTS PlanningTasks (
  TaskId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  OwnerId TEXT NOT NULL,
  Title TEXT NOT NULL,
  Notes TEXT NULL,
  DueDate TEXT NULL,
  IsDone INTEGER NOT NULL DEFAULT 0,
  CompletedAt TEXT NULL,
  VendorId INTEGER NULL,
  SortOrder INTEGER NOT NULL DEFAULT 0,
  CreatedAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_PlanningTasks_OwnerId ON PlanningTasks (OwnerId);

-- Phase 3.A — Seating constraints (together/apart pairs)
CREATE TABLE IF NOT EXISTS SeatingConstraints (
  ConstraintId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  OwnerId TEXT NOT NULL,
  GuestAId INTEGER NOT NULL,
  GuestBId INTEGER NOT NULL,
  Kind INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_SeatingConstraints_OwnerId ON SeatingConstraints (OwnerId);
CREATE UNIQUE INDEX IF NOT EXISTS IX_SeatingConstraints_Unique ON SeatingConstraints (OwnerId, GuestAId, GuestBId, Kind);

-- Phase 3.B — Visual floor plan (table positions + venue elements)
-- NOTE: these 3 ALTER TABLE lines were applied once by hand (verified with
-- PRAGMA table_info('Tables') first) and are commented out so this file
-- stays safely re-runnable. If setting up a brand-new database, skip them:
-- EF's Table model (and Database.EnsureCreated()) already includes these
-- columns for a fresh DB.
-- ALTER TABLE Tables ADD COLUMN PositionX REAL NULL;
-- ALTER TABLE Tables ADD COLUMN PositionY REAL NULL;
-- ALTER TABLE Tables ADD COLUMN Rotation REAL NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS VenueElements (
  ElementId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  OwnerId TEXT NOT NULL,
  Kind INTEGER NOT NULL,
  Label TEXT NULL,
  X REAL NOT NULL,
  Y REAL NOT NULL,
  Width REAL NOT NULL DEFAULT 80,
  Height REAL NOT NULL DEFAULT 80,
  Rotation REAL NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS IX_VenueElements_OwnerId ON VenueElements (OwnerId);

-- Phase 4.A — Event-day check-in
-- NOTE: applied once by hand (see the ALTER TABLE guidance above) —
-- ALTER TABLE Guests ADD COLUMN CheckedInAt TEXT NULL;
-- ALTER TABLE Guests ADD COLUMN CheckedInCount INTEGER NULL;

-- Phase 4.B — Live photo wall
-- NOTE: applied once by hand (see the ALTER TABLE guidance above) —
-- ALTER TABLE Clients ADD COLUMN WallToken TEXT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS IX_Clients_WallToken ON Clients (WallToken);

-- Phase 5.A — Automatic RSVP reminders
-- NOTE: applied once by hand (see the ALTER TABLE guidance above) —
-- ALTER TABLE Clients ADD COLUMN AutoRemindersEnabled INTEGER NOT NULL DEFAULT 0;
-- ALTER TABLE Clients ADD COLUMN ReminderOffsetsJson TEXT NULL;

-- Phase 5.B — Collaborators
CREATE TABLE IF NOT EXISTS EventCollaborators (
  CollaboratorId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  OwnerId TEXT NOT NULL,
  CollaboratorUserId TEXT NULL,
  CollaboratorEmail TEXT NOT NULL,
  Role INTEGER NOT NULL DEFAULT 1,
  InviteToken TEXT NOT NULL,
  InvitedAt TEXT NOT NULL,
  ExpiresAt TEXT NOT NULL,
  AcceptedAt TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_EventCollaborators_Owner_Email ON EventCollaborators (OwnerId, CollaboratorEmail);
CREATE UNIQUE INDEX IF NOT EXISTS IX_EventCollaborators_InviteToken ON EventCollaborators (InviteToken);

-- Example pattern for a future ALTER TABLE ... ADD COLUMN — check first
-- (SQLite has no "ADD COLUMN IF NOT EXISTS", and a RAISE(IGNORE) guard does
-- NOT work outside a trigger — verified against sqlite3 3.43):
--
-- sqlite3 eventimage.db "PRAGMA table_info('Guests');" | grep CheckedInAt
-- # only if that prints nothing:
-- sqlite3 eventimage.db "ALTER TABLE Guests ADD COLUMN CheckedInAt TEXT NULL;"
--
-- Example pattern for a new table (CREATE TABLE IF NOT EXISTS works fine
-- in SQLite):
--
-- CREATE TABLE IF NOT EXISTS PlanningTasks (
--   TaskId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
--   OwnerId TEXT NOT NULL,
--   Title TEXT NOT NULL,
--   ...
-- );
-- CREATE INDEX IF NOT EXISTS IX_PlanningTasks_OwnerId ON PlanningTasks (OwnerId);
