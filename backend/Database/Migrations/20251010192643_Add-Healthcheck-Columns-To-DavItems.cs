using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddHealthcheckColumnsToDavItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastHealthCheck",
                table: "DavItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "NextHealthCheck",
                table: "DavItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReleaseDate",
                table: "DavItems",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastHealthCheck",
                table: "DavItems");

            migrationBuilder.DropColumn(
                name: "NextHealthCheck",
                table: "DavItems");

            migrationBuilder.DropColumn(
                name: "ReleaseDate",
                table: "DavItems");
        }
    }
}
