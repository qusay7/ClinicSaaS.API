using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueUniqueNumberIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QueueEntries_ClinicId",
                table: "QueueEntries");

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_ClinicId_Date_QueueNumber",
                table: "QueueEntries",
                columns: new[] { "ClinicId", "Date", "QueueNumber" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QueueEntries_ClinicId_Date_QueueNumber",
                table: "QueueEntries");

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_ClinicId",
                table: "QueueEntries",
                column: "ClinicId");
        }
    }
}
