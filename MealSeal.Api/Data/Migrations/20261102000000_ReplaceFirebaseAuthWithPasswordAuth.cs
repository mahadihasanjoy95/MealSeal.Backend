using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealSeal.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MealSealDbContext))]
    [Migration("20261102000000_ReplaceFirebaseAuthWithPasswordAuth")]
    public partial class ReplaceFirebaseAuthWithPasswordAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The users table only ever held the handful of Firebase-UID-keyed placeholder rows;
            // there's no real user data to migrate, so it's simplest and safest to recreate it
            // under the new email/password shape rather than try to carry old rows forward.
            migrationBuilder.DropTable(name: "users");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RestaurantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<System.DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "users");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    FirebaseUid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RestaurantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<System.DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.FirebaseUid);
                });
        }
    }
}
