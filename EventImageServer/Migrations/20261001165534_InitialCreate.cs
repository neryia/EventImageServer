using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventImageServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    TotalBudget = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    FullName = table.Column<string>(type: "TEXT", nullable: true),
                    Role = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RsvpDeadline = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EventDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WallToken = table.Column<string>(type: "TEXT", nullable: true),
                    AutoRemindersEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReminderOffsetsJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BudgetCategories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    BudgetId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    PlannedAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    LinkedVendorCategory = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetCategories_Budgets_BudgetId",
                        column: x => x.BudgetId,
                        principalTable: "Budgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Albums",
                columns: table => new
                {
                    AlbumId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Theme = table.Column<string>(type: "TEXT", nullable: false),
                    CoverFileName = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Albums", x => x.AlbumId);
                    table.ForeignKey(
                        name: "FK_Albums_Clients_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventCollaborators",
                columns: table => new
                {
                    CollaboratorId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    CollaboratorUserId = table.Column<string>(type: "TEXT", nullable: true),
                    CollaboratorEmail = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    InviteToken = table.Column<string>(type: "TEXT", nullable: false),
                    InvitedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventCollaborators", x => x.CollaboratorId);
                    table.ForeignKey(
                        name: "FK_EventCollaborators_Clients_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GuestCategories",
                columns: table => new
                {
                    GuestCategoryId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    Color = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuestCategories", x => x.GuestCategoryId);
                    table.ForeignKey(
                        name: "FK_GuestCategories_Clients_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OwnerMedia",
                columns: table => new
                {
                    OwnerMediaId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    MediaType = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ShowOnWall = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerMedia", x => x.OwnerMediaId);
                    table.ForeignKey(
                        name: "FK_OwnerMedia_Clients_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanningTasks",
                columns: table => new
                {
                    TaskId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    DueDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDone = table.Column<bool>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VendorId = table.Column<int>(type: "INTEGER", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningTasks", x => x.TaskId);
                    table.ForeignKey(
                        name: "FK_PlanningTasks_Clients_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeatingConstraints",
                columns: table => new
                {
                    ConstraintId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    GuestAId = table.Column<int>(type: "INTEGER", nullable: false),
                    GuestBId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeatingConstraints", x => x.ConstraintId);
                    table.ForeignKey(
                        name: "FK_SeatingConstraints_Clients_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tables",
                columns: table => new
                {
                    TableId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Shape = table.Column<string>(type: "TEXT", nullable: true),
                    Tag = table.Column<string>(type: "TEXT", nullable: false),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    CapacityOnSides = table.Column<int>(type: "INTEGER", nullable: false),
                    CapacityOnTopAndBottom = table.Column<int>(type: "INTEGER", nullable: false),
                    PositionX = table.Column<double>(type: "REAL", nullable: true),
                    PositionY = table.Column<double>(type: "REAL", nullable: true),
                    Rotation = table.Column<double>(type: "REAL", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tables", x => x.TableId);
                    table.ForeignKey(
                        name: "FK_Tables_Clients_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserMedia",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: true),
                    FileName = table.Column<string>(type: "TEXT", nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", nullable: true),
                    MediaType = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserMedia_Clients_UserId",
                        column: x => x.UserId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Vendors",
                columns: table => new
                {
                    VendorId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ContactName = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Phone = table.Column<string>(type: "TEXT", nullable: false),
                    WhatsApp = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    Website = table.Column<string>(type: "TEXT", nullable: false),
                    Instagram = table.Column<string>(type: "TEXT", nullable: false),
                    AgreedPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    DepositAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    NextPaymentDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    QuestionsToAsk = table.Column<string>(type: "TEXT", nullable: false),
                    Promises = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendors", x => x.VendorId);
                    table.ForeignKey(
                        name: "FK_Vendors_Clients_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VenueElements",
                columns: table => new
                {
                    ElementId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: true),
                    X = table.Column<double>(type: "REAL", nullable: false),
                    Y = table.Column<double>(type: "REAL", nullable: false),
                    Width = table.Column<double>(type: "REAL", nullable: false),
                    Height = table.Column<double>(type: "REAL", nullable: false),
                    Rotation = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VenueElements", x => x.ElementId);
                    table.ForeignKey(
                        name: "FK_VenueElements_Clients_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BudgetExpenses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    BudgetId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VendorId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetExpenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetExpenses_BudgetCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "BudgetCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BudgetExpenses_Budgets_BudgetId",
                        column: x => x.BudgetId,
                        principalTable: "Budgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AlbumMedia",
                columns: table => new
                {
                    AlbumMediaId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AlbumId = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlbumMedia", x => x.AlbumMediaId);
                    table.ForeignKey(
                        name: "FK_AlbumMedia_Albums_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Albums",
                        principalColumn: "AlbumId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Guests",
                columns: table => new
                {
                    GuestId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", nullable: true),
                    Tag = table.Column<string>(type: "TEXT", nullable: false),
                    NumberOfGuests = table.Column<int>(type: "INTEGER", nullable: false),
                    TableId = table.Column<int>(type: "INTEGER", nullable: true),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: true),
                    Phone = table.Column<string>(type: "TEXT", nullable: true),
                    RsvpStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    RsvpToken = table.Column<string>(type: "TEXT", nullable: true),
                    RsvpTokenCreatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RsvpOpenedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RsvpRespondedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConfirmedCount = table.Column<int>(type: "INTEGER", nullable: true),
                    OptedOut = table.Column<bool>(type: "INTEGER", nullable: false),
                    MealCountsJson = table.Column<string>(type: "TEXT", nullable: true),
                    RsvpNote = table.Column<string>(type: "TEXT", nullable: true),
                    GuestPhotoUploadCount = table.Column<int>(type: "INTEGER", nullable: false),
                    GuestVideoUploadCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CheckedInAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CheckedInCount = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Guests", x => x.GuestId);
                    table.ForeignKey(
                        name: "FK_Guests_Clients_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Guests_Tables_TableId",
                        column: x => x.TableId,
                        principalTable: "Tables",
                        principalColumn: "TableId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "VendorAttachments",
                columns: table => new
                {
                    AttachmentId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VendorId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorAttachments", x => x.AttachmentId);
                    table.ForeignKey(
                        name: "FK_VendorAttachments_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "VendorId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VendorTimelineSteps",
                columns: table => new
                {
                    StepId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VendorId = table.Column<int>(type: "INTEGER", nullable: false),
                    Step = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDone = table.Column<bool>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorTimelineSteps", x => x.StepId);
                    table.ForeignKey(
                        name: "FK_VendorTimelineSteps_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "VendorId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GuestMedia",
                columns: table => new
                {
                    GuestMediaId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuestId = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: true),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    MediaType = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuestMedia", x => x.GuestMediaId);
                    table.ForeignKey(
                        name: "FK_GuestMedia_Guests_GuestId",
                        column: x => x.GuestId,
                        principalTable: "Guests",
                        principalColumn: "GuestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MessageLogs",
                columns: table => new
                {
                    MessageLogId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: true),
                    GuestId = table.Column<int>(type: "INTEGER", nullable: false),
                    Channel = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    To = table.Column<string>(type: "TEXT", nullable: false),
                    TwilioSid = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", nullable: true),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageLogs", x => x.MessageLogId);
                    table.ForeignKey(
                        name: "FK_MessageLogs_Clients_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MessageLogs_Guests_GuestId",
                        column: x => x.GuestId,
                        principalTable: "Guests",
                        principalColumn: "GuestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlbumMedia_AlbumId",
                table: "AlbumMedia",
                column: "AlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_Albums_OwnerId",
                table: "Albums",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetCategories_BudgetId",
                table: "BudgetCategories",
                column: "BudgetId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetExpenses_BudgetId",
                table: "BudgetExpenses",
                column: "BudgetId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetExpenses_CategoryId",
                table: "BudgetExpenses",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_WallToken",
                table: "Clients",
                column: "WallToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventCollaborators_InviteToken",
                table: "EventCollaborators",
                column: "InviteToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventCollaborators_OwnerId_CollaboratorEmail",
                table: "EventCollaborators",
                columns: new[] { "OwnerId", "CollaboratorEmail" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuestCategories_OwnerId_Value",
                table: "GuestCategories",
                columns: new[] { "OwnerId", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuestMedia_GuestId",
                table: "GuestMedia",
                column: "GuestId");

            migrationBuilder.CreateIndex(
                name: "IX_Guests_OwnerId",
                table: "Guests",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Guests_RsvpToken",
                table: "Guests",
                column: "RsvpToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Guests_TableId",
                table: "Guests",
                column: "TableId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageLogs_GuestId",
                table: "MessageLogs",
                column: "GuestId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageLogs_OwnerId",
                table: "MessageLogs",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerMedia_OwnerId",
                table: "OwnerMedia",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningTasks_OwnerId",
                table: "PlanningTasks",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_SeatingConstraints_OwnerId",
                table: "SeatingConstraints",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_SeatingConstraints_OwnerId_GuestAId_GuestBId_Kind",
                table: "SeatingConstraints",
                columns: new[] { "OwnerId", "GuestAId", "GuestBId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tables_OwnerId",
                table: "Tables",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMedia_UserId",
                table: "UserMedia",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorAttachments_VendorId",
                table: "VendorAttachments",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_OwnerId",
                table: "Vendors",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorTimelineSteps_VendorId",
                table: "VendorTimelineSteps",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_VenueElements_OwnerId",
                table: "VenueElements",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlbumMedia");

            migrationBuilder.DropTable(
                name: "BudgetExpenses");

            migrationBuilder.DropTable(
                name: "EventCollaborators");

            migrationBuilder.DropTable(
                name: "GuestCategories");

            migrationBuilder.DropTable(
                name: "GuestMedia");

            migrationBuilder.DropTable(
                name: "MessageLogs");

            migrationBuilder.DropTable(
                name: "OwnerMedia");

            migrationBuilder.DropTable(
                name: "PlanningTasks");

            migrationBuilder.DropTable(
                name: "SeatingConstraints");

            migrationBuilder.DropTable(
                name: "UserMedia");

            migrationBuilder.DropTable(
                name: "VendorAttachments");

            migrationBuilder.DropTable(
                name: "VendorTimelineSteps");

            migrationBuilder.DropTable(
                name: "VenueElements");

            migrationBuilder.DropTable(
                name: "Albums");

            migrationBuilder.DropTable(
                name: "BudgetCategories");

            migrationBuilder.DropTable(
                name: "Guests");

            migrationBuilder.DropTable(
                name: "Vendors");

            migrationBuilder.DropTable(
                name: "Budgets");

            migrationBuilder.DropTable(
                name: "Tables");

            migrationBuilder.DropTable(
                name: "Clients");
        }
    }
}
