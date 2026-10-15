using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPrescriptionDispensing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DispensedAt",
                table: "VisitNotes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DispensedBy",
                table: "VisitNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDispensed",
                table: "VisitNotes",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DispensedAt",
                table: "VisitNotes");

            migrationBuilder.DropColumn(
                name: "DispensedBy",
                table: "VisitNotes");

            migrationBuilder.DropColumn(
                name: "IsDispensed",
                table: "VisitNotes");
        }
    }
}
