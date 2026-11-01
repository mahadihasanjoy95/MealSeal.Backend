using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealSeal.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MealSealDbContext))]
    [Migration("20261102010000_AddVoucherQrPng")]
    public partial class AddVoucherQrPng : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "QrPng",
                table: "vouchers",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QrPng",
                table: "vouchers");
        }
    }
}
