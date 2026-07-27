using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class UpdateStaffRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Department",
                table: "Staff");

            migrationBuilder.RenameColumn(
                name: "Role",
                table: "Staff",
                newName: "StaffRole");

            migrationBuilder.AddColumn<Guid>(
                name: "DoctorId",
                table: "Staff",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RoleId",
                table: "Staff",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Staff_DepartmentId",
                table: "Staff",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Staff_DoctorId",
                table: "Staff",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_Staff_RoleId",
                table: "Staff",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Staff_Departments_DepartmentId",
                table: "Staff",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Staff_Doctors_DoctorId",
                table: "Staff",
                column: "DoctorId",
                principalTable: "Doctors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Staff_Roles_RoleId",
                table: "Staff",
                column: "RoleId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Staff_Departments_DepartmentId",
                table: "Staff");

            migrationBuilder.DropForeignKey(
                name: "FK_Staff_Doctors_DoctorId",
                table: "Staff");

            migrationBuilder.DropForeignKey(
                name: "FK_Staff_Roles_RoleId",
                table: "Staff");

            migrationBuilder.DropIndex(
                name: "IX_Staff_DepartmentId",
                table: "Staff");

            migrationBuilder.DropIndex(
                name: "IX_Staff_DoctorId",
                table: "Staff");

            migrationBuilder.DropIndex(
                name: "IX_Staff_RoleId",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "DoctorId",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "RoleId",
                table: "Staff");

            migrationBuilder.RenameColumn(
                name: "StaffRole",
                table: "Staff",
                newName: "Role");

            migrationBuilder.AddColumn<string>(
                name: "Department",
                table: "Staff",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
