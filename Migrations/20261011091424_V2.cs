using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notariat.Api.Migrations
{
    public partial class V2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheminFichier",
                table: "Declarations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodeQ",
                table: "Declarations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Penalite",
                table: "Declarations",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Prix",
                table: "Declarations",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "CategorieQs",
                columns: table => new
                {
                    CodeCategQ = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LibelleCategQFr = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LibelleCategQAr = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategorieQs", x => x.CodeCategQ);
                });

            migrationBuilder.CreateTable(
                name: "DetailQs",
                columns: table => new
                {
                    CodeQ = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CodeCategQ = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LibelleQFr = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LibelleQAr = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Tarif = table.Column<int>(type: "int", nullable: false),
                    Taux = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DetailQs", x => x.CodeQ);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategorieQs");

            migrationBuilder.DropTable(
                name: "DetailQs");

            migrationBuilder.DropColumn(
                name: "CheminFichier",
                table: "Declarations");

            migrationBuilder.DropColumn(
                name: "CodeQ",
                table: "Declarations");

            migrationBuilder.DropColumn(
                name: "Penalite",
                table: "Declarations");

            migrationBuilder.DropColumn(
                name: "Prix",
                table: "Declarations");
        }
    }
}
