using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameLibrary.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddManualPickerExclusion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_excluded_from_picker",
                table: "library_entries",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_excluded_from_picker",
                table: "library_entries");
        }
    }
}
