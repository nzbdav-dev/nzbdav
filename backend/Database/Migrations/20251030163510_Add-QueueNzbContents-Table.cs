using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueNzbContentsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QueueNzbContents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NzbContents = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueNzbContents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueNzbContents_QueueItems_Id",
                        column: x => x.Id,
                        principalTable: "QueueItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Copy existing NzbContents from QueueItems to QueueNzbContents
            migrationBuilder.Sql(@"
                INSERT INTO QueueNzbContents (Id, NzbContents)
                SELECT Id, NzbContents FROM QueueItems
                WHERE NzbContents IS NOT NULL
            ");

            
            migrationBuilder.DropColumn(
                name: "NzbContents",
                table: "QueueItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NzbContents",
                table: "QueueItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
            
            // Populate the re-introduced column from QueueNzbContents
            migrationBuilder.Sql(@"
                UPDATE QueueItems
                SET NzbContents = (
                    SELECT q.NzbContents
                    FROM QueueNzbContents q
                    WHERE q.Id = QueueItems.Id
                )
                WHERE Id IN (SELECT Id FROM QueueNzbContents);
            ");
            
            migrationBuilder.DropTable(
                name: "QueueNzbContents");
        }
    }
}
