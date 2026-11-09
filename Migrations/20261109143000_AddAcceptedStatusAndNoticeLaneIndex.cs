using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace apptrack.Migrations
{
    /// <inheritdoc />
    public partial class AddAcceptedStatusAndNoticeLaneIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_application_notices_Status",
                table: "application_notices");

            migrationBuilder.AddCheckConstraint(
                name: "CK_application_notices_Status",
                table: "application_notices",
                sql: "\"Status\" IN ('Application Sent', 'Interviewing', 'Accepted', 'Rejected')");

            migrationBuilder.CreateIndex(
                name: "IX_application_notices_lane",
                table: "application_notices",
                columns: new[] { "UserId", "Status", "SentAt", "Id" },
                descending: new[] { false, false, true, true },
                filter: "\"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_application_notices_lane",
                table: "application_notices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_application_notices_Status",
                table: "application_notices");

            migrationBuilder.AddCheckConstraint(
                name: "CK_application_notices_Status",
                table: "application_notices",
                sql: "\"Status\" IN ('Application Sent', 'Interviewing', 'Rejected')");
        }
    }
}
