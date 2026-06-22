using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkTypeColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Doctors_Departments_DepartmentId1",
                table: "Doctors");

            migrationBuilder.DropIndex(
                name: "IX_Doctors_DepartmentId1",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "DepartmentId1",
                table: "Doctors");

            migrationBuilder.AddColumn<string>(
                name: "WorkType",
                table: "Doctors",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkType",
                table: "Doctors");

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId1",
                table: "Doctors",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Doctors_DepartmentId1",
                table: "Doctors",
                column: "DepartmentId1");

            migrationBuilder.AddForeignKey(
                name: "FK_Doctors_Departments_DepartmentId1",
                table: "Doctors",
                column: "DepartmentId1",
                principalTable: "Departments",
                principalColumn: "Id");
        }
    }
}
