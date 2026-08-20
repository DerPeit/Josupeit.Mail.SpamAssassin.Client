// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;
using NUnit.Framework;

namespace Josupeit.Mail.SpamAssassin.Client.UnitTests;

[TestFixture]
public sealed class TellInstructionTests
{
    [Test]
    public void Learning_as_spam_declares_spam_and_touches_only_the_local_database()
    {
        var instruction = TellInstruction.LearnAsSpam;

        Assert.That(instruction.MessageClass, Is.EqualTo(MessageClass.Spam));
        Assert.That(instruction.Set, Is.EqualTo(TellDatabases.Local));
        Assert.That(instruction.Remove, Is.EqualTo(TellDatabases.None));
    }

    [Test]
    public void Forgetting_declares_no_class_because_none_is_needed()
    {
        var instruction = TellInstruction.Forget;

        Assert.That(instruction.MessageClass, Is.Null);
        Assert.That(instruction.Remove, Is.EqualTo(TellDatabases.Local));
    }

    [Test]
    public void Revoking_as_ham_learns_locally_and_withdraws_remotely()
    {
        var instruction = TellInstruction.RevokeAsHam;

        Assert.That(instruction.MessageClass, Is.EqualTo(MessageClass.Ham));
        Assert.That(instruction.Set, Is.EqualTo(TellDatabases.Local));
        Assert.That(instruction.Remove, Is.EqualTo(TellDatabases.Remote));
    }

    [Test]
    public void The_named_instructions_are_the_same_instance_every_time()
    {
        var first = TellInstruction.LearnAsSpam;
        var second = TellInstruction.LearnAsSpam;

        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void An_instruction_that_would_ask_for_nothing_is_refused()
    {
        Assert.Throws<ArgumentException>(() => TellInstruction.Create(MessageClass.Spam, TellDatabases.None, TellDatabases.None));
    }

    // Without a class the daemon reads a local write as "learn this as ham", which is a wrong answer arrived at quietly.
    [Test]
    public void Writing_locally_without_a_class_is_refused()
    {
        var refused = Assert.Throws<ArgumentException>(() => TellInstruction.Create(null, TellDatabases.Local, TellDatabases.None))!;

        Assert.That(refused.ParamName, Is.EqualTo("messageClass"));
    }

    [Test]
    public void Removing_without_a_class_is_allowed()
    {
        var instruction = TellInstruction.Create(null, TellDatabases.None, TellDatabases.Local | TellDatabases.Remote);

        Assert.That(instruction.Remove, Is.EqualTo(TellDatabases.Local | TellDatabases.Remote));
    }

    [Test]
    public void Moving_a_message_from_one_database_to_the_other_is_allowed()
    {
        var instruction = TellInstruction.Create(MessageClass.Spam, TellDatabases.Local, TellDatabases.Remote);

        Assert.That(instruction.Set, Is.EqualTo(TellDatabases.Local));
        Assert.That(instruction.Remove, Is.EqualTo(TellDatabases.Remote));
    }

    [TestCase(4)]
    [TestCase(-1)]
    public void A_database_the_enumeration_does_not_declare_is_refused(int databases)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TellInstruction.Create(MessageClass.Spam, (TellDatabases)databases, TellDatabases.None));
    }

    [TestCase(8)]
    [TestCase(-2)]
    public void A_database_to_remove_that_the_enumeration_does_not_declare_is_refused(int databases)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TellInstruction.Create(MessageClass.Spam, TellDatabases.Local, (TellDatabases)databases));
    }

    [Test]
    public void A_class_the_enumeration_does_not_declare_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TellInstruction.Create((MessageClass)7, TellDatabases.Local, TellDatabases.None));
    }
}
