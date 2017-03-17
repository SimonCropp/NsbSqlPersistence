﻿using System;
using System.Text;

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

        public static SubscriptionCommands Build(SqlVariant sqlVariant, string tablePrefix, string schema)
        {
            string tableName;

            switch (sqlVariant)
            {
                case SqlVariant.MsSqlServer:
                    tableName = $"[{schema}].[{tablePrefix}SubscriptionData]";
                    break;

                case SqlVariant.MySql:
                    tableName = $"`{tablePrefix}SubscriptionData`";
                    break;

                case SqlVariant.Oracle:
                    tableName = $"\"{tablePrefix}SS\"";
                    break;

                default:
                    throw new Exception($"Unknown SqlVariant: {sqlVariant}.");
            }
            var subscribeCommand = GetSubscribeCommand(sqlVariant, tableName);
            var unsubscribeCommand = GetUnsubscribeCommand(sqlVariant, tableName);
            var getSubscribers = GetSubscribersFunc(sqlVariant, tablePrefix);

            return new SubscriptionCommands(
                subscribe: subscribeCommand,
                unsubscribe: unsubscribeCommand,
                getSubscribers: getSubscribers);
        }

        static string GetSubscribeCommand(SqlVariant sqlVariant, string tableName)
        {
            switch (sqlVariant)
            {
                case SqlVariant.MsSqlServer:
                    return $@"
declare @dummy int;
merge {tableName} with (holdlock) as target
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

                case SqlVariant.MySql:
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

                case SqlVariant.Oracle:
                    return $@"
begin
    insert into {tableName}
    (
        MessageType,
        Subscriber,
        Endpoint,
        PersistenceVersion
    )
    values
    (
        :1,
        :2,
        :3,
        :4
    );
    commit;
exception
    when DUP_VAL_ON_INDEX
    then ROLLBACK;
end;
";

                default:
                    throw new Exception($"Unknown SqlVariant: {sqlVariant}.");
            }
        }

        static string GetUnsubscribeCommand(SqlVariant sqlVariant, string tableName)
        {
            switch (sqlVariant)
            {
                case SqlVariant.Oracle:
                    return $@"
delete from {tableName}
where
    Subscriber = :1 and
    MessageType = :2";

                default:
                    return $@"
delete from {tableName}
where
    Subscriber = @Subscriber and
    MessageType = @MessageType";
            }
        }

        static Func<List<MessageType>, string> GetSubscribersFunc(SqlVariant sqlVariant, string tablePrefix)
        {
            switch (sqlVariant)
            {
                case SqlVariant.Oracle:

                    var getSubscribersPrefixOracle = $@"
select distinct Subscriber, Endpoint
from {tablePrefix}SS
where MessageType in (";

                    return messageTypes =>
                    {
                        var builder = new StringBuilder(getSubscribersPrefixOracle);
                        for (var i = 0; i < messageTypes.Count; i++)
                        {
                            var paramName = $":{i}";
                            builder.Append(paramName);
                            if (i < messageTypes.Count - 1)
                            {
                                builder.Append(", ");
                            }
                        }
                        builder.Append(")");
                        return builder.ToString();
                    };

                default:

                    var getSubscribersPrefix = $@"
select distinct Subscriber, Endpoint
from {tablePrefix}SubscriptionData
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
}