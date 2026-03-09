using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using NzbWebDAV.Database.MigrationHelpers.Attributes;

namespace NzbWebDAV.Database.MigrationHelpers.Operations;

[ExecuteAfter]
public class ArbitrarySqlAfter(string sql) : BaseMigrationOperation
{
    public string Sql { get; } = sql;

    public override void Generate(MigrationsSqlGeneratorDependencies dependencies, IModel? model, MigrationCommandListBuilder builder)
    {
        builder.Append(Sql);
        builder.EndCommand();
    }
}
