using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace NzbWebDAV.Database.MigrationHelpers;

public abstract class BaseMigrationOperation : MigrationOperation
{
    public abstract void Generate(
        MigrationsSqlGeneratorDependencies dependencies,
        IModel? model,
        MigrationCommandListBuilder builder);
}