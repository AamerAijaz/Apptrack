using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace apptrack.Migrations
{
    /// <inheritdoc />
    public partial class RememberAppliedMailboxMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "applied_mailbox_messages",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    NoticeId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applied_mailbox_messages", x => new { x.UserId, x.MessageId });
                    table.ForeignKey(
                        name: "FK_applied_mailbox_messages_application_notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "application_notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_applied_mailbox_messages_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_applied_mailbox_messages_NoticeId",
                table: "applied_mailbox_messages",
                column: "NoticeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "applied_mailbox_messages");
        }
    }
}
