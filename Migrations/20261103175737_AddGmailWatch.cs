using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace apptrack.Migrations
{
    /// <inheritdoc />
    public partial class AddGmailWatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gmail_watches",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    HistoryId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    WatchExpiration = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gmail_watches", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_gmail_watches_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gmail_watches");
        }
    }
}
