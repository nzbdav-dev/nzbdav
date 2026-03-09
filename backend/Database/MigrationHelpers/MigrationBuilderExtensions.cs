using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;
using NzbWebDAV.Database.MigrationHelpers.Operations;

namespace NzbWebDAV.Database.MigrationHelpers;

public static class MigrationBuilderExtensions
{
    extension(MigrationBuilder migrationBuilder)
    {
        private OperationBuilder<TOperation> AddOperation<TOperation>(TOperation operation)
            where TOperation : MigrationOperation
        {
            migrationBuilder.Operations.Add(operation);
            return new OperationBuilder<TOperation>(operation);
        }

        /// <summary>
        /// Execute arbitrary SQL before EF Core's standard migration operations (including table rebuilds).
        /// </summary>
        public OperationBuilder<ArbitrarySqlBefore> SqlBefore(string sql)
            => migrationBuilder.AddOperation(new ArbitrarySqlBefore(sql));

        /// <summary>
        /// Execute arbitrary SQL after EF Core's standard migration operations (including table rebuilds).
        /// Use this for trigger creation when the migration includes DropForeignKey or other table-rebuilding operations.
        /// </summary>
        public OperationBuilder<ArbitrarySqlAfter> SqlAfter(string sql)
            => migrationBuilder.AddOperation(new ArbitrarySqlAfter(sql));
    }
}