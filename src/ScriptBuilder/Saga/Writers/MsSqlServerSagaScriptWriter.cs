using System.IO;
using System.Text;
using NServiceBus.Persistence.Sql.ScriptBuilder;

class MsSqlServerSagaScriptWriter : ISagaScriptWriter
{
    TextWriter writer;
    SagaDefinition saga;

    public MsSqlServerSagaScriptWriter(TextWriter textWriter, SagaDefinition saga)
    {
        writer = textWriter;
        this.saga = saga;
    }

    public void Initialize()
    {
    }

    public void WriteTableNameVariable()
    {
        writer.WriteLine($@"
declare @tableName nvarchar(max) = '[' + @schema + '].[' + @tablePrefix + N'{saga.TableSuffix}]';
declare @tableNameWithoutSchema nvarchar(max) = @tablePrefix + N'{saga.TableSuffix}';
");
    }

    public void CreateComplete()
    {
    }

    public void AddProperty(CorrelationProperty correlationProperty)
    {
        var columnType = MsSqlServerCorrelationPropertyTypeConverter.GetColumnType(correlationProperty.Type);
        var name = correlationProperty.Name;
        writer.Write($@"
if not exists
(
  select * from sys.columns
  where
    name = N'Correlation_{name}' and
    object_id = object_id(@tableName)
)
begin
  declare @createColumn_{name} nvarchar(max);
  set @createColumn_{name} = '
  alter table ' + @tableName + N'
    add Correlation_{name} {columnType};';
  exec(@createColumn_{name});
end
");
    }

    public void VerifyColumnType(CorrelationProperty correlationProperty)
    {
        string columnType;
        if (correlationProperty.Type == CorrelationPropertyType.String)
        {
            columnType = "nvarchar";
        }
        else
        {
            columnType = MsSqlServerCorrelationPropertyTypeConverter.GetColumnType(correlationProperty.Type);
        }
        var name = correlationProperty.Name;
        writer.Write($@"
declare @dataType_{name} nvarchar(max);
set @dataType_{name} = (
  select data_type
  from INFORMATION_SCHEMA.COLUMNS
  where
    table_name = @tableNameWithoutSchema and
    table_schema = @schema and
    column_name = 'Correlation_{name}'
);
if (@dataType_{name} <> '{columnType}')
  begin
    declare @error_{name} nvarchar(max) = N'Incorrect data type for Correlation_{name}. Expected {columnType} got ' + @dataType_{name} + '.';
    throw 50000, @error_{name}, 0
  end
");
    }

    public void WriteCreateIndex(CorrelationProperty correlationProperty)
    {
        var name = correlationProperty.Name;
        writer.Write($@"
if not exists
(
    select *
    from sys.indexes
    where
        name = N'Index_Correlation_{name}' and
        object_id = object_id(@tableName)
)
begin
  declare @createIndex_{name} nvarchar(max);
  set @createIndex_{name} = N'
  create unique index Index_Correlation_{name}
  on ' + @tableName + N'(Correlation_{name})
  where Correlation_{name} is not null;';
  exec(@createIndex_{name});
end
");
    }

    public void WritePurgeObsoleteProperties()
    {
        var builder = new StringBuilder();

        if (saga.CorrelationProperty != null)
        {
            builder.Append($" and\r\n        column_name <> N'Correlation_{saga.CorrelationProperty.Name}'");
        }
        if (saga.TransitionalCorrelationProperty != null)
        {
            builder.Append($" and\r\n        column_name <> N'Correlation_{saga.TransitionalCorrelationProperty.Name}'");
        }

        writer.Write($@"
declare @dropPropertiesQuery nvarchar(max);
select @dropPropertiesQuery =
(
    select 'alter table ' + @tableName + ' drop column ' + column_name + ';'
    from INFORMATION_SCHEMA.COLUMNS
    where
        table_name = @tableNameWithoutSchema and
        table_schema = @schema and
        column_name like 'Correlation_%'{builder}
);
exec sp_executesql @dropPropertiesQuery
");
    }

    public void WritePurgeObsoleteIndex()
    {
        var builder = new StringBuilder();

        if (saga.CorrelationProperty != null)
        {
            builder.Append($" and\r\n        Name <> N'Index_Correlation_{saga.CorrelationProperty.Name}'");
        }
        if (saga.TransitionalCorrelationProperty != null)
        {
            builder.Append($" and\r\n        Name <> N'Index_Correlation_{saga.TransitionalCorrelationProperty.Name}'");
        }

        writer.Write($@"
declare @dropIndexQuery nvarchar(max);
select @dropIndexQuery =
(
    select 'drop index ' + name + ' on ' + @tableName + ';'
    from sysindexes
    where
        Id = object_id(@tableName) and
        Name is not null and
        Name like 'Index_Correlation_%'{builder}
);
exec sp_executesql @dropIndexQuery
");
    }

    public void WriteCreateTable()
    {
        writer.Write(@"
if not exists
(
    select *
    from sys.objects
    where
        object_id = object_id(@tableName) and
        type in ('U')
)
begin
declare @createTable nvarchar(max);
set @createTable = '
    create table ' + @tableName + '(
        Id uniqueidentifier not null primary key nonclustered,
        Metadata nvarchar(max) not null,
        Data nvarchar(max) not null,
        PersistenceVersion varchar(23) not null,
        SagaTypeVersion varchar(23) not null,
        Concurrency int not null
    )
';
exec(@createTable);
end
");
    }

    public void WriteDropTable()
    {
        writer.Write(@"
if exists
(
    select *
    from sys.objects
    where
        object_id = object_id(@tableName)
        and type in ('U')
)
begin
    declare @dropTable nvarchar(max);
    set @dropTable = 'drop table ' + @tableName;
    exec(@dropTable);
end
");
    }
}
