using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PickNBook.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAboutUsCms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "about_us",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AboutDescription = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Module = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WhoWeAreHeading = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WhoWeAreDescription = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WhoWeAreImageUrl = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_about_us", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "about_us_counts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AboutUsId = table.Column<int>(type: "int", nullable: false),
                    CountValue = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountTitle = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_about_us_counts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_about_us_counts_about_us_AboutUsId",
                        column: x => x.AboutUsId,
                        principalTable: "about_us",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "about_us_team_members",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AboutUsId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Designation = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ImageUrl = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_about_us_team_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_about_us_team_members_about_us_AboutUsId",
                        column: x => x.AboutUsId,
                        principalTable: "about_us",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "about_us",
                columns: new[] { "Id", "AboutDescription", "CreatedAtUtc", "Module", "Status", "UpdatedAtUtc", "WhoWeAreDescription", "WhoWeAreHeading", "WhoWeAreImageUrl" },
                values: new object[] { 1, "<p>Pick N Book is a leading travel booking provider delivering flights and bus bookings to travelers worldwide.</p>", new DateTime(2026, 6, 17, 0, 0, 0, 0, DateTimeKind.Utc), "B2C", "active", new DateTime(2026, 6, 17, 0, 0, 0, 0, DateTimeKind.Utc), "<p>We are a dedicated team of travel enthusiasts and product engineers building seamless transport bookings.</p>", "Who We Are", "/uploads/about/who.png" });

            migrationBuilder.InsertData(
                table: "about_us_counts",
                columns: new[] { "Id", "AboutUsId", "CountTitle", "CountValue", "DisplayOrder" },
                values: new object[,]
                {
                    { 1, 1, "Years", "6+", 1 },
                    { 2, 1, "Travel Partners", "100+", 2 },
                    { 3, 1, "Product Managers", "16+", 3 },
                    { 4, 1, "Customer Support", "24/7", 4 }
                });

            migrationBuilder.InsertData(
                table: "about_us_team_members",
                columns: new[] { "Id", "AboutUsId", "Designation", "DisplayOrder", "ImageUrl", "Name" },
                values: new object[,]
                {
                    { 1, 1, "Lead Developer", 1, "/uploads/team/naveen.png", "Naveen" },
                    { 2, 1, "Project Manager", 2, "/uploads/team/default.png", "Rajesh" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_about_us_Module",
                table: "about_us",
                column: "Module",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_about_us_counts_AboutUsId",
                table: "about_us_counts",
                column: "AboutUsId");

            migrationBuilder.CreateIndex(
                name: "IX_about_us_team_members_AboutUsId",
                table: "about_us_team_members",
                column: "AboutUsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "about_us_counts");

            migrationBuilder.DropTable(
                name: "about_us_team_members");

            migrationBuilder.DropTable(
                name: "about_us");
        }
    }
}
