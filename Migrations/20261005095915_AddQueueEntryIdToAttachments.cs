using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueEntryIdToAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "QueueEntryId",
                table: "Attachments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_QueueEntryId",
                table: "Attachments",
                column: "QueueEntryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Attachments_QueueEntries_QueueEntryId",
                table: "Attachments",
                column: "QueueEntryId",
                principalTable: "QueueEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attachments_QueueEntries_QueueEntryId",
                table: "Attachments");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_QueueEntryId",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "QueueEntryId",
                table: "Attachments");
        }
    }
}
