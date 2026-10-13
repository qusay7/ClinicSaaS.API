using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueEntryBillingToPaymentDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentDetails_AppointmentId",
                table: "PaymentDetails");

            migrationBuilder.AlterColumn<Guid>(
                name: "AppointmentId",
                table: "PaymentDetails",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "QueueEntryId",
                table: "PaymentDetails",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentDetails_AppointmentId",
                table: "PaymentDetails",
                column: "AppointmentId",
                unique: true,
                filter: "[AppointmentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentDetails_QueueEntryId",
                table: "PaymentDetails",
                column: "QueueEntryId",
                unique: true,
                filter: "[QueueEntryId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentDetails_QueueEntries_QueueEntryId",
                table: "PaymentDetails",
                column: "QueueEntryId",
                principalTable: "QueueEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentDetails_QueueEntries_QueueEntryId",
                table: "PaymentDetails");

            migrationBuilder.DropIndex(
                name: "IX_PaymentDetails_AppointmentId",
                table: "PaymentDetails");

            migrationBuilder.DropIndex(
                name: "IX_PaymentDetails_QueueEntryId",
                table: "PaymentDetails");

            migrationBuilder.DropColumn(
                name: "QueueEntryId",
                table: "PaymentDetails");

            migrationBuilder.AlterColumn<Guid>(
                name: "AppointmentId",
                table: "PaymentDetails",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentDetails_AppointmentId",
                table: "PaymentDetails",
                column: "AppointmentId",
                unique: true);
        }
    }
}
