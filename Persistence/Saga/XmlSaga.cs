﻿
namespace NServiceBus.SqlPersistence
{
    public abstract class XmlSaga<TSagaData> : Saga<TSagaData>
        where TSagaData : 
        XmlSagaData, 
        IContainSagaData, 
        new()
    {
        //TODO: throw in MSBuild if this is overriden in child class
        protected override void ConfigureHowToFindSaga(SagaPropertyMapper<TSagaData> mapper)
        {
            var messagePropertyMapper = new MessagePropertyMapper<TSagaData>(mapper);
            ConfigureMapping(messagePropertyMapper);
        }

        protected virtual void ConfigureMapping(MessagePropertyMapper<TSagaData> mapper)
        {
        }

    }
}