using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace c_dataCache3.Migrations
{
    /// <inheritdoc />
    public partial class AddIsActiveToBinanceApiKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ApiKeys",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ApiKeys");
        }
    }
}
