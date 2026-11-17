using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddNursingToVisitProcedureItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NeedsNursing",
                table: "VisitProcedureItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NursingDone",
                table: "VisitProcedureItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "NursingDoneAt",
                table: "VisitProcedureItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NursingDoneBy",
                table: "VisitProcedureItems",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NeedsNursing",
                table: "VisitProcedureItems");

            migrationBuilder.DropColumn(
                name: "NursingDone",
                table: "VisitProcedureItems");

            migrationBuilder.DropColumn(
                name: "NursingDoneAt",
                table: "VisitProcedureItems");

            migrationBuilder.DropColumn(
                name: "NursingDoneBy",
                table: "VisitProcedureItems");
        }
    }
}
