using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GW2Stonks.Migrations
{
    /// <inheritdoc />
    public partial class AddItemVolume : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemVolumes",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    SoldPerDay = table.Column<int>(type: "int", nullable: false),
                    BoughtPerDay = table.Column<int>(type: "int", nullable: false),
                    SupplyNow = table.Column<int>(type: "int", nullable: false),
                    DemandNow = table.Column<int>(type: "int", nullable: false),
                    CoverageHours = table.Column<double>(type: "double", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemVolumes", x => x.ItemId);
                    table.ForeignKey(
                        name: "FK_ItemVolumes_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemVolumes");
        }
    }
}
