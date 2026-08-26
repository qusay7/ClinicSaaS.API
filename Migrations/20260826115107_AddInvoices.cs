using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicSaaS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultTaxMethod",
                table: "Clinics",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultTaxRate",
                table: "Clinics",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "LastInvoiceNumber",
                table: "Clinics",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "PricesIncludeTax",
                table: "Clinics",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TaxRegistrationType",
                table: "Clinics",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaymentDetailId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,9)", precision: 18, scale: 9, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,9)", precision: 18, scale: 9, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,9)", precision: 18, scale: 9, nullable: false),
                    PayableAmount = table.Column<decimal>(type: "decimal(18,9)", precision: 18, scale: 9, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSubmitted = table.Column<bool>(type: "bit", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvoiceXml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TaxResponse = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QrCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TaxMethod = table.Column<int>(type: "int", nullable: false),
                    TaxRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Invoices_Invoices_SourceInvoiceId",
                        column: x => x.SourceInvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Invoices_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Invoices_PaymentDetails_PaymentDetailId",
                        column: x => x.PaymentDetailId,
                        principalTable: "PaymentDetails",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InvoiceItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,9)", precision: 18, scale: 9, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,9)", precision: 18, scale: 9, nullable: false),
                    Discount = table.Column<decimal>(type: "decimal(18,9)", precision: 18, scale: 9, nullable: false),
                    TaxRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,9)", precision: 18, scale: 9, nullable: false),
                    TaxType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceItems_InvoiceItems_SourceItemId",
                        column: x => x.SourceItemId,
                        principalTable: "InvoiceItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvoiceItems_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvoiceItems_TreatmentPlanTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "TreatmentPlanTemplates",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_InvoiceId",
                table: "InvoiceItems",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_SourceItemId",
                table: "InvoiceItems",
                column: "SourceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceItems_TemplateId",
                table: "InvoiceItems",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ClinicId_InvoiceNumber",
                table: "Invoices",
                columns: new[] { "ClinicId", "InvoiceNumber" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PatientId",
                table: "Invoices",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PaymentDetailId",
                table: "Invoices",
                column: "PaymentDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SourceInvoiceId",
                table: "Invoices",
                column: "SourceInvoiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceItems");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropColumn(
                name: "DefaultTaxMethod",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "DefaultTaxRate",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "LastInvoiceNumber",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "PricesIncludeTax",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "TaxRegistrationType",
                table: "Clinics");
        }
    }
}
