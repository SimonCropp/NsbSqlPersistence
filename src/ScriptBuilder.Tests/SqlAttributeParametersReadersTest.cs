﻿using System.Collections.Generic;
using NUnit.Framework;

[TestFixture]
public class SqlAttributeParametersReadersTest
{
    [Test]
    public void Minimal()
    {
        var result = SettingsAttributeReader.ReadFromAttribute(
            new CustomAttributeMock(
                new Dictionary<string, object>
                {
                    {
                        //At least one is required
                        "MsSqlServerScripts", true
                    }
                }));

        Assert.IsNotNull(result);
        TestApprover.VerifyWithJson(result);
    }

    [Test]
    public void Defaults()
    {
        var result = SettingsAttributeReader.ReadFromAttribute(null);
        Assert.IsNotNull(result);
        TestApprover.VerifyWithJson(result);
    }

    [Test]
    public void NonDefaults()
    {
        var result = SettingsAttributeReader.ReadFromAttribute(
            new CustomAttributeMock(
                new Dictionary<string, object>
                {
                    {
                        "ScriptPromotionPath", @"D:\scripts"
                    },
                    {
                        "MsSqlServerScripts", true
                    },
                    {
                        "MySqlScripts", true
                    },
                    {
                        "OracleScripts", true
                    },
                    {
                        "ProduceSagaScripts", false
                    },
                    {
                        "ProduceTimeoutScripts", false
                    },
                    {
                        "ProduceSubscriptionScripts", false
                    },
                    {
                        "ProduceOutboxScripts", false
                    }
                }));
        Assert.IsNotNull(result);
        TestApprover.VerifyWithJson(result);
    }


}