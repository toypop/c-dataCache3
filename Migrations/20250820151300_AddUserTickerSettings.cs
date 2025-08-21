using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace c_dataCache3.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTickerSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserTickerSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserSubscribedTickerId = table.Column<int>(type: "integer", nullable: false),
                    DeclinePercentage = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SelectedKlineInterval = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTickerSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTickerSettings_UserSubscribedTickers_UserSubscribedTick~",
                        column: x => x.UserSubscribedTickerId,
                        principalTable: "UserSubscribedTickers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserTickerSettings_UserSubscribedTickerId",
                table: "UserTickerSettings",
                column: "UserSubscribedTickerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserTickerSettings");
        }
    }
}
