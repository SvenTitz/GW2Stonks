using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GW2Stonks.Migrations
{
    /// <inheritdoc />
    public partial class DropVolumeCoverageHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoverageHours",
                table: "ItemVolumes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CoverageHours",
                table: "ItemVolumes",
                type: "double",
                nullable: false,
                defaultValue: 0.0);
        }
    }
}
