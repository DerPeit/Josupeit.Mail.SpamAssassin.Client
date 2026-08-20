// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System.Collections.Generic;
using NUnit.Framework;

namespace Josupeit.Mail.SpamAssassin.Client.UnitTests;

[TestFixture]
public sealed class SymbolsResultTests
{
    private static readonly SpamVerdict _verdict = new(IsSpam: true, Score: 8.4, Threshold: 5.0);

    [Test]
    public void Two_results_listing_the_same_rules_are_equal()
    {
        var first = new SymbolsResult(_verdict, ["GTUBE", "BAYES_99"]);
        var second = new SymbolsResult(_verdict, new List<string> { "GTUBE", "BAYES_99" });

        Assert.That(second, Is.EqualTo(first));
        Assert.That(second == first, Is.True);
    }

    [Test]
    public void Equal_results_hash_the_same()
    {
        var first = new SymbolsResult(_verdict, ["GTUBE", "BAYES_99"]);
        var second = new SymbolsResult(_verdict, ["GTUBE", "BAYES_99"]);

        Assert.That(second.GetHashCode(), Is.EqualTo(first.GetHashCode()));
    }

    [Test]
    public void Equal_results_land_on_the_same_entry_of_a_set()
    {
        var symbols = new HashSet<SymbolsResult>
        {
            new(_verdict, ["GTUBE", "BAYES_99"]),
            new(_verdict, ["GTUBE", "BAYES_99"]),
        };

        Assert.That(symbols, Has.Count.EqualTo(1));
    }

    // Order is part of the answer: two orders are two different reports rather than one.
    [Test]
    public void The_same_rules_in_a_different_order_are_not_equal()
    {
        var first = new SymbolsResult(_verdict, ["GTUBE", "BAYES_99"]);
        var second = new SymbolsResult(_verdict, ["BAYES_99", "GTUBE"]);

        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void A_different_rule_makes_a_different_result()
    {
        var first = new SymbolsResult(_verdict, ["GTUBE"]);
        var second = new SymbolsResult(_verdict, ["GTUBE", "NO_RELAYS"]);

        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void A_different_verdict_makes_a_different_result()
    {
        var first = new SymbolsResult(_verdict, ["GTUBE"]);
        var second = new SymbolsResult(new(IsSpam: false, Score: 0.1, Threshold: 5.0), ["GTUBE"]);

        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void A_result_equals_itself_without_walking_the_list()
    {
        var result = new SymbolsResult(_verdict, ["GTUBE"]);

        Assert.That(result.Equals(result), Is.True);
    }

    [Test]
    public void A_result_is_not_equal_to_nothing()
    {
        var result = new SymbolsResult(_verdict, ["GTUBE"]);

        Assert.That(result.Equals(null), Is.False);
    }
}
