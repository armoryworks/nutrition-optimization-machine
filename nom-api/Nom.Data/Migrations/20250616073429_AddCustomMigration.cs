using Microsoft.EntityFrameworkCore.Migrations;
using Nom.Data;

#nullable disable

namespace Nom.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.ApplyCustomUpOperations();
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.ApplyCustomDownOperations();
        }
    }
}
