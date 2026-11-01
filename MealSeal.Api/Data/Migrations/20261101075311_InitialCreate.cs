using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MealSeal.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VoucherId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    ActorUid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    FirebaseUid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RestaurantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.FirebaseUid);
                });

            migrationBuilder.CreateTable(
                name: "vouchers",
                columns: table => new
                {
                    VoucherId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    RecipientRef = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    RestaurantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MealType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    IssuedAt = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<long>(type: "bigint", nullable: false),
                    IssuerUid = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    PayloadSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Signature = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Algorithm = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    KeyId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RedeemedByUid = table.Column<string>(type: "text", nullable: true),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByUid = table.Column<string>(type: "text", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vouchers", x => x.VoucherId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_VoucherId",
                table: "audit_events",
                column: "VoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_vouchers_Status_ExpiresAt",
                table: "vouchers",
                columns: new[] { "Status", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "vouchers");
        }
    }
}
