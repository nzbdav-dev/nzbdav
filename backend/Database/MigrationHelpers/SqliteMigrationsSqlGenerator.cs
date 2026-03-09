using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using NzbWebDAV.Database.MigrationHelpers.Attributes;

namespace NzbWebDAV.Database.MigrationHelpers;

/// <summary>
/// Custom migrations SQL generator that reorders operations marked with ExecuteBefore/ExecuteAfter attributes.
/// This ensures that custom SQL operations run at the correct time relative to EF Core's table rebuilds.
/// </summary>
public class SqliteMigrationsSqlGenerator<TGenerator> : IMigrationsSqlGenerator
    where TGenerator : IMigrationsSqlGenerator
{
    public TGenerator BaseGenerator { get; }
    protected MigrationsSqlGeneratorDependencies Dependencies { get; }

    public SqliteMigrationsSqlGenerator(MigrationsSqlGeneratorDependencies dependencies,
        IRelationalAnnotationProvider migrationsAnnotations)
    {
        if (Activator.CreateInstance(typeof(TGenerator), [dependencies, migrationsAnnotations]) is TGenerator generator)
        {
            BaseGenerator = generator;
            Dependencies = dependencies;
        }
        else
        {
            throw new MissingMethodException(
                $"{typeof(TGenerator)} is missing a constructor ({typeof(MigrationsSqlGeneratorDependencies)}, {typeof(IRelationalAnnotationProvider)})");
        }
    }

    protected IReadOnlyList<MigrationCommand> Generate(List<MigrationOperation> operations, IModel? model)
    {
        var builder = new MigrationCommandListBuilder(Dependencies);
        foreach (var operation in operations.OfType<BaseMigrationOperation>())
        {
            operation.Generate(Dependencies, model, builder);
        }

        return builder.GetCommandList();
    }

    public IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
    {
        var middleOperations = operations.ToList();

        // Extract operations marked with ExecuteBefore
        var operationsToExecuteBefore = middleOperations
            .Where(o => o.GetType().CustomAttributes.Any(a => a.AttributeType == typeof(ExecuteBeforeAttribute)))
            .ToList();
        operationsToExecuteBefore.ForEach(o => middleOperations.Remove(o));

        // Extract operations marked with ExecuteAfter
        var operationsToExecuteAfter = middleOperations
            .Where(o => o.GetType().CustomAttributes.Any(a => a.AttributeType == typeof(ExecuteAfterAttribute)))
            .ToList();
        operationsToExecuteAfter.ForEach(o => middleOperations.Remove(o));

        // Generate SQL in order: before -> middle (EF Core) -> after
        var before = Generate(operationsToExecuteBefore, model);
        var middle = BaseGenerator.Generate(middleOperations, model, options);
        var after = Generate(operationsToExecuteAfter, model);

        // Combine and return
        var combined = new List<MigrationCommand>();
        combined.AddRange(before);
        combined.AddRange(middle);
        combined.AddRange(after);

        return combined;
    }
}