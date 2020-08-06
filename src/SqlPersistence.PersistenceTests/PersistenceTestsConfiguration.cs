﻿namespace NServiceBus.PersistenceTesting
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading.Tasks;
    using Extensibility;
    using Newtonsoft.Json;
    using Npgsql;
    using NpgsqlTypes;
    using NServiceBus.Outbox;
    using NServiceBus.Sagas;
    using NUnit.Framework;
    using Persistence;
    using Persistence.Sql.ScriptBuilder;
    using Transport;

    public partial class PersistenceTestsConfiguration
    {
        public bool SupportsDtc => false; // TODO: this should actually be set to true, tests still failing
        public bool SupportsOutbox => true;
        public bool SupportsFinders => true;
        public bool SupportsPessimisticConcurrency { get; set; }

        public ISagaIdGenerator SagaIdGenerator { get; private set; }
        public ISagaPersister SagaStorage { get; private set; }
        public ISynchronizedStorage SynchronizedStorage { get; private set; }
        public ISynchronizedStorageAdapter SynchronizedStorageAdapter { get; private set; }
        public IOutboxStorage OutboxStorage { get; private set; }

        static PersistenceTestsConfiguration()
        {
            var postgreSql = new SqlDialect.PostgreSql();
            postgreSql.JsonBParameterModifier = parameter =>
            {
                var npgsqlParameter = (NpgsqlParameter)parameter;
                npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
            };

            var outboxVariants = new List<object>
            {
                CreateVariant(new SqlDialect.MsSqlServer(), BuildSqlDialect.MsSqlServer, MsSqlMicrosoftDataClientConnectionBuilder.Build),
                CreateVariant(postgreSql, BuildSqlDialect.PostgreSql, PostgreSqlConnectionBuilder.Build),
                CreateVariant(new SqlDialect.MySql(), BuildSqlDialect.MySql, MySqlConnectionBuilder.Build),
            };

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OracleConnectionString")))
            {
                outboxVariants.Add(CreateVariant(new SqlDialect.Oracle(), BuildSqlDialect.Oracle, OracleConnectionBuilder.Build));
            }

            var sagaVariants = new List<object>() {outboxVariants};

            sagaVariants.Add(CreateVariant(new SqlDialect.MsSqlServer(), BuildSqlDialect.MsSqlServer, MsSqlMicrosoftDataClientConnectionBuilder.Build, true));
            sagaVariants.Add(CreateVariant(postgreSql, BuildSqlDialect.PostgreSql, PostgreSqlConnectionBuilder.Build, true));
            sagaVariants.Add(CreateVariant(new SqlDialect.MySql(), BuildSqlDialect.MySql, MySqlConnectionBuilder.Build, true));
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OracleConnectionString")))
            {
                sagaVariants.Add(CreateVariant(new SqlDialect.Oracle(), BuildSqlDialect.Oracle, OracleConnectionBuilder.Build, true));
            }

            SagaVariants = sagaVariants.ToArray();
            OutboxVariants = outboxVariants.ToArray();
        }

        static TestFixtureData CreateVariant(SqlDialect dialect, BuildSqlDialect buildDialect, Func<DbConnection> connectionFactory, bool usePessimisticMode = false)
        {
            return new TestFixtureData(new TestVariant(new SqlTestVariant(dialect, buildDialect, connectionFactory, usePessimisticMode)));
        }

        public Task Configure()
        {
            var variant = (SqlTestVariant)Variant.Values[0];
            var dialect = variant.Dialect;
            var buildDialect = variant.BuildDialect;
            var connectionFactory = variant.ConnectionFactory;
            var pessimisticMode = variant.UsePessimisticMode;

            if (SessionTimeout.HasValue)
            {
                dialect = new TimeoutSettingDialect(dialect, (int)SessionTimeout.Value.TotalSeconds);
            }

            var infoCache = new SagaInfoCache(
                null,
                Serializer.JsonSerializer,
                reader => new JsonTextReader(reader),
                writer => new JsonTextWriter(writer),
                "PersistenceTests_",
                dialect,
                SagaMetadataCollection,
                name => ShortenSagaName(name));

            var connectionManager = new ConnectionManager(connectionFactory);
            SagaIdGenerator = new DefaultSagaIdGenerator();
            SagaStorage = new SagaPersister(infoCache, dialect);
            SynchronizedStorage = new SynchronizedStorage(connectionManager, infoCache, null);
            SynchronizedStorageAdapter = new StorageAdapter(connectionManager, infoCache, dialect, null);
            OutboxStorage = CreateOutboxPersister(connectionManager, dialect, false, false);
            SupportsPessimisticConcurrency = pessimisticMode;

            GetContextBagForSagaStorage = () =>
            {
                var contextBag = new ContextBag();
                contextBag.Set(new IncomingMessage("MessageId", new Dictionary<string, string>(), new byte[0]));
                return contextBag;
            };

            GetContextBagForOutbox = () =>
            {
                var contextBag = new ContextBag();
                contextBag.Set(new IncomingMessage("MessageId", new Dictionary<string, string>(), new byte[0]));
                return contextBag;
            };

            using (var connection = connectionFactory())
            {
                connection.Open();

                foreach (var saga in SagaMetadataCollection)
                {
                    CorrelationProperty correlationProperty = null;
                    if (saga.TryGetCorrelationProperty(out var propertyMetadata))
                    {
                        //TODO: Hard-code correlation property to string
                        correlationProperty = new CorrelationProperty(propertyMetadata.Name, CorrelationPropertyType.String);
                    }

                    var tableName = ShortenSagaName(saga.SagaType.Name);
                    var definition = new SagaDefinition(tableName, saga.EntityName, correlationProperty);

                    connection.ExecuteCommand(SagaScriptBuilder.BuildDropScript(definition, buildDialect), "PersistenceTests");
                    connection.ExecuteCommand(SagaScriptBuilder.BuildCreateScript(definition, buildDialect), "PersistenceTests");
                }

                connection.ExecuteCommand(OutboxScriptBuilder.BuildDropScript(buildDialect), "PersistenceTests");
                connection.ExecuteCommand(OutboxScriptBuilder.BuildCreateScript(buildDialect), "PersistenceTests");
            }
            return Task.CompletedTask;
        }

        static string ShortenSagaName(string sagaName)
        {
            return sagaName
                .Replace("AnotherSagaWithCorrelatedProperty", "ASWCP")
                .Replace("SagaWithCorrelationProperty", "SWCP")
                .Replace("SagaWithoutCorrelationProperty", "SWOCP")
                .Replace("SagaWithComplexType", "SWCT")
                .Replace("TestSaga", "TS");
        }

        static OutboxPersister CreateOutboxPersister(IConnectionManager connectionManager, SqlDialect sqlDialect, bool pessimisticMode, bool transactionScopeMode)
        {
            var outboxCommands = OutboxCommandBuilder.Build(sqlDialect, "PersistenceTests_");
            ConcurrencyControlStrategy concurrencyControlStrategy;
            if (pessimisticMode)
            {
                concurrencyControlStrategy = new PessimisticConcurrencyControlStrategy(sqlDialect, outboxCommands);
            }
            else
            {
                concurrencyControlStrategy = new OptimisticConcurrencyControlStrategy(sqlDialect, outboxCommands);
            }

            ISqlOutboxTransaction transactionFactory()
            {
                return transactionScopeMode
                    ? (ISqlOutboxTransaction)new TransactionScopeSqlOutboxTransaction(concurrencyControlStrategy, connectionManager)
                    : new AdoNetSqlOutboxTransaction(concurrencyControlStrategy, connectionManager);
            }

            var outboxPersister = new OutboxPersister(connectionManager, sqlDialect, outboxCommands, transactionFactory);
            return outboxPersister;
        }

        public Task Cleanup()
        {
            return Task.CompletedTask;
        }
    }
}