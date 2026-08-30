using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class Phase3_FinalBlockers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_SupplierFulfillmentExecutions",
                table: "SupplierFulfillmentExecutions");

            migrationBuilder.RenameTable(
                name: "SupplierFulfillmentExecutions",
                newName: "supplier_fulfillment_executions");

            migrationBuilder.AlterColumn<string>(
                name: "RefundStatus",
                table: "payments",
                type: "varchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "FulfillmentStatus",
                table: "payments",
                type: "varchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddPrimaryKey(
                name: "PK_supplier_fulfillment_executions",
                table: "supplier_fulfillment_executions",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_payments_FulfillmentStatus",
                table: "payments",
                column: "FulfillmentStatus");

            migrationBuilder.CreateIndex(
                name: "IX_payments_RefundStatus",
                table: "payments",
                column: "RefundStatus");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_fulfillment_executions_PaymentId",
                table: "supplier_fulfillment_executions",
                column: "PaymentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_FulfillmentStatus",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_payments_RefundStatus",
                table: "payments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_supplier_fulfillment_executions",
                table: "supplier_fulfillment_executions");

            migrationBuilder.DropIndex(
                name: "IX_supplier_fulfillment_executions_PaymentId",
                table: "supplier_fulfillment_executions");

            migrationBuilder.RenameTable(
                name: "supplier_fulfillment_executions",
                newName: "SupplierFulfillmentExecutions");

            migrationBuilder.AlterColumn<string>(
                name: "RefundStatus",
                table: "payments",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "FulfillmentStatus",
                table: "payments",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(255)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SupplierFulfillmentExecutions",
                table: "SupplierFulfillmentExecutions",
                column: "Id");
        }
    }
}
