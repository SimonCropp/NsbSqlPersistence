#pragma warning disable 672 // overrides obsolete
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
namespace NServiceBus
{
    using System;
    using System.Text;

    public partial class SqlDialect
    {
        public partial class MsSqlServer
        {
            internal override string GetSagaTableName(string tablePrefix, string tableSuffix)
            {
                return $"[{Schema}].[{tablePrefix}{tableSuffix}]";
            }

            internal override object BuildSagaData(CommandWrapper command, RuntimeSagaInfo sagaInfo, IContainSagaData sagaData)
            {
                var writer = command.LeaseWriter();
                sagaInfo.ToJson(sagaData, writer);
                return writer.ToCharSegment();
            }

            internal override string BuildSaveCommand(string correlationProperty, string transitionalCorrelationProperty, string tableName)
            {
                var valuesBuilder = new StringBuilder();
                var insertBuilder = new StringBuilder();

                if (correlationProperty != null)
                {
                    insertBuilder.Append($",\r\n    Correlation_{correlationProperty}");
                    valuesBuilder.Append(",\r\n    @CorrelationId");
                }
                if (transitionalCorrelationProperty != null)
                {
                    insertBuilder.Append($",\r\n    Correlation_{transitionalCorrelationProperty}");
                    valuesBuilder.Append(",\r\n    @TransitionalCorrelationId");
                }

                return $@"
insert into {tableName}
(
    Id,
    Metadata,
    Data,
    PersistenceVersion,
    SagaTypeVersion,
    Concurrency{insertBuilder}
)
values
(
    @Id,
    @Metadata,
    @Data,
    @PersistenceVersion,
    @SagaTypeVersion,
    1{valuesBuilder}
)";
            }

            internal override string BuildUpdateCommand(string transitionalCorrelationProperty, string tableName)
            {
                // no need to set CorrelationProperty since it is immutable
                var correlationSet = "";
                if (transitionalCorrelationProperty != null)
                {
                    correlationSet = $",\r\n    Correlation_{transitionalCorrelationProperty} = @TransitionalCorrelationId";
                }

                return $@"
update {tableName}
set
    Data = @Data,
    PersistenceVersion = @PersistenceVersion,
    SagaTypeVersion = @SagaTypeVersion,
    Concurrency = @Concurrency + 1{correlationSet}
where
    Id = @Id and Concurrency = @Concurrency
";
            }

            internal override string BuildGetBySagaIdCommand(string tableName)
            {
                return $@"
select
    Id,
    SagaTypeVersion,
    Concurrency,
    Metadata,
    Data
from {tableName}
with (updlock,rowlock)
where Id = @Id
";
            }

            internal override string BuildGetByPropertyCommand(string propertyName, string tableName)
            {
                return $@"
select
    Id,
    SagaTypeVersion,
    Concurrency,
    Metadata,
    Data
from {tableName}
with (updlock,rowlock)
where Correlation_{propertyName} = @propertyValue
";
            }

            internal override string BuildCompleteCommand(string tableName)
            {
                return $@"
delete from {tableName}
where Id = @Id and Concurrency = @Concurrency
";
            }

            internal override Func<string, string> BuildSelectFromCommand(string tableName)
            {
                return whereClause => $@"
select
    Id,
    SagaTypeVersion,
    Concurrency,
    Metadata,
    Data
from {tableName}
with (updlock,rowlock)
where {whereClause}
";
            }
        }
    }
}
