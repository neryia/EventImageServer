using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventImageServer.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VendorPayments",
                columns: table => new
                {
                    PaymentId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VendorId = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsPaid = table.Column<bool>(type: "INTEGER", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorPayments", x => x.PaymentId);
                    table.ForeignKey(
                        name: "FK_VendorPayments_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "VendorId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VendorPayments_VendorId",
                table: "VendorPayments",
                column: "VendorId");

            // Convert the legacy single-date model into schedule rows so
            // PaidAmount / NextPaymentDate (now derived) keep their values.
            migrationBuilder.Sql(@"
INSERT INTO VendorPayments (VendorId, Label, Amount, DueDate, IsPaid, PaidAt)
SELECT VendorId, 'Paid before schedule', PaidAmount, CreatedAt, 1, CreatedAt
FROM Vendors WHERE PaidAmount > 0;");
            migrationBuilder.Sql(@"
INSERT INTO VendorPayments (VendorId, Label, Amount, DueDate, IsPaid, PaidAt)
SELECT VendorId, 'Next payment', AgreedPrice - PaidAmount, NextPaymentDate, 0, NULL
FROM Vendors WHERE NextPaymentDate IS NOT NULL AND AgreedPrice - PaidAmount > 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VendorPayments");
        }
    }
}
