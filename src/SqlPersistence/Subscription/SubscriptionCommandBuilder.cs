﻿using System;
using System.Text;
#pragma warning disable 1591

namespace NServiceBus.Persistence.Sql
{
    using System.Collections.Generic;
    using Unicast.Subscriptions;

    /// <summary>
    /// Not for public use.
    /// </summary>
    [Obsolete("Not for public use")]
    public static class SubscriptionCommandBuilder
    {

        public static SubscriptionCommands Build(Type sqlVariant, string tablePrefix, string schema)
        {
            string tableName;

            if (sqlVariant == typeof(SqlDialect.MsSqlServer))
            {
                tableName = $"[{schema}].[{tablePrefix}SubscriptionData]";
            }
            else if (sqlVariant == typeof(SqlDialect.MySql))
            {
                tableName = $"`{tablePrefix}SubscriptionData`";
            }
            else if (sqlVariant == typeof(SqlDialect.Oracle))
            {
                tableName = $"{tablePrefix.ToUpper()}SS";
            }
            else
            {
                throw new Exception($"Unknown SqlVariant: {sqlVariant}.");
            }

            var subscribeCommand = GetSubscribeCommand(sqlVariant, tableName);
            var unsubscribeCommand = GetUnsubscribeCommand(sqlVariant, tableName);
            var getSubscribers = GetSubscribersFunc(sqlVariant, tableName);

            return new SubscriptionCommands(
                subscribe: subscribeCommand,
                unsubscribe: unsubscribeCommand,
                getSubscribers: getSubscribers);
        }

        static string GetSubscribeCommand(Type sqlVariant, string tableName)
        {
            if (sqlVariant == typeof(SqlDialect.MsSqlServer))
            {
                return $@"
declare @dummy int;
merge {tableName} with (holdlock, tablock) as target
using(select @Endpoint as Endpoint, @Subscriber as Subscriber, @MessageType as MessageType) as source
on target.Endpoint = source.Endpoint and
   target.Subscriber = source.Subscriber and
   target.MessageType = source.MessageType
when matched then
    update set @dummy = 0
when not matched then
insert
(
    Subscriber,
    MessageType,
    Endpoint,
    PersistenceVersion
)
values
(
    @Subscriber,
    @MessageType,
    @Endpoint,
    @PersistenceVersion
);";
            }

            if (sqlVariant == typeof(SqlDialect.MySql))
            {
                return $@"
insert into {tableName}
(
    Subscriber,
    MessageType,
    Endpoint,
    PersistenceVersion
)
values
(
    @Subscriber,
    @MessageType,
    @Endpoint,
    @PersistenceVersion
)
on duplicate key update
    Endpoint = @Endpoint,
    PersistenceVersion = @PersistenceVersion
";
            }

            if (sqlVariant == typeof(SqlDialect.Oracle))
            {
                return $@"
begin
    insert into ""{tableName}""
    (
        MessageType,
        Subscriber,
        Endpoint,
        PersistenceVersion
    )
    values
    (
        :MessageType,
        :Subscriber,
        :Endpoint,
        :PersistenceVersion
    );
    commit;
exception
    when DUP_VAL_ON_INDEX
    then ROLLBACK;
end;
";
            }
            
             throw new Exception($"Unknown SqlVariant: {sqlVariant}.");
        }

        static string GetUnsubscribeCommand(Type sqlVariant, string tableName)
        {
            if (sqlVariant == typeof(SqlDialect.Oracle))
            {
                return $@"
delete from ""{tableName}""
where
    Subscriber = :Subscriber and
    MessageType = :MessageType";
            }

            return $@"
delete from {tableName}
where
    Subscriber = @Subscriber and
    MessageType = @MessageType";
        }


        static Func<List<MessageType>, string> GetSubscribersFunc(Type sqlVariant, string tableName)
        {
            if (sqlVariant == typeof(SqlDialect.Oracle))
            {
                var getSubscribersPrefixOracle = $@"
select distinct Subscriber, Endpoint
from ""{tableName}""
where MessageType in (";

                return messageTypes =>
                {
                    var builder = new StringBuilder(getSubscribersPrefixOracle);
                    for (var i = 0; i < messageTypes.Count; i++)
                    {
                        var paramName = $":type{i}";
                        builder.Append(paramName);
                        if (i < messageTypes.Count - 1)
                        {
                            builder.Append(", ");
                        }
                    }
                    builder.Append(")");
                    return builder.ToString();
                };
            }

            var getSubscribersPrefix = $@"
select distinct Subscriber, Endpoint
from {tableName}
where MessageType in (";

                    return messageTypes =>
                    {
                        var builder = new StringBuilder(getSubscribersPrefix);
                        for (var i = 0; i < messageTypes.Count; i++)
                        {
                            var paramName = $"@type{i}";
                            builder.Append(paramName);
                            if (i < messageTypes.Count - 1)
                            {
                                builder.Append(", ");
                            }
                        }
                        builder.Append(")");
                        return builder.ToString();
                    };
            }
    }
}