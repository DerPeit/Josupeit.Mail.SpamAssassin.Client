// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// What a tell request is supposed to do to the daemon's databases.
/// </summary>
/// <remarks>
/// <para>
/// The protocol has one command for teaching the daemon anything, and what that command does is decided by
/// three values: the class the message is being declared to be, the databases to write to, and the databases
/// to erase it from. Learning, forgetting, reporting and revoking are combinations of those three rather than
/// separate operations, which is why they are named here as factories instead of as methods on the client.
/// </para>
/// <para>
/// The five named ones cover what callers actually ask for; <see cref="Create"/> exists for the combinations
/// they do not, such as moving a message from one database to the other in a single request.
/// </para>
/// </remarks>
public sealed record TellInstruction
{
    private TellInstruction(MessageClass? messageClass, TellDatabases set, TellDatabases remove)
    {
        MessageClass = messageClass;
        Set = set;
        Remove = remove;
    }

    // The enum is namespace-qualified below because here MessageClass is the property, and an initializer may not
    // reach an instance member (CS0236).

    /// <summary>
    /// Teach the daemon that this message is spam, in its own Bayes database and nowhere else.
    /// </summary>
    public static TellInstruction LearnAsSpam { get; } = new(Client.MessageClass.Spam, TellDatabases.Local, TellDatabases.None);

    /// <summary>
    /// Teach the daemon that this message is legitimate, in its own Bayes database and nowhere else.
    /// </summary>
    public static TellInstruction LearnAsHam { get; } = new(Client.MessageClass.Ham, TellDatabases.Local, TellDatabases.None);

    /// <summary>
    /// Undo whatever the daemon learned about this message, in its own Bayes database.
    /// </summary>
    /// <remarks>
    /// No class is named, and none is needed: forgetting is symmetric, and the daemon knows which way it once
    /// leaned. A message it never learned is not an error - it simply reports having done nothing.
    /// </remarks>
    public static TellInstruction Forget { get; } = new(messageClass: null, TellDatabases.None, TellDatabases.Local);

    /// <summary>
    /// Report this message as spam to the collaborative networks as well as to the local database.
    /// </summary>
    /// <remarks>
    /// This leaves the machine. Reporting publishes a judgement about the message to every other user of the
    /// networks the daemon is configured for, so it belongs to a deliberate decision by a person or a policy,
    /// not to an automatic reaction to a score.
    /// </remarks>
    public static TellInstruction ReportAsSpam { get; } = new(Client.MessageClass.Spam, TellDatabases.Local | TellDatabases.Remote, TellDatabases.None);

    /// <summary>
    /// Declare this message legitimate: learn it as such locally, and withdraw any report of it from the
    /// collaborative networks.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="ReportAsSpam"/>, and the way to correct it after the fact.
    /// </remarks>
    public static TellInstruction RevokeAsHam { get; } = new(Client.MessageClass.Ham, TellDatabases.Local, TellDatabases.Remote);

    /// <summary>
    /// The class the message is being declared to be, or <see langword="null"/> when the instruction does not
    /// declare one.
    /// </summary>
    public MessageClass? MessageClass { get; }

    /// <summary>
    /// The databases the message is written to.
    /// </summary>
    public TellDatabases Set { get; }

    /// <summary>
    /// The databases the message is erased from.
    /// </summary>
    public TellDatabases Remove { get; }

    /// <summary>
    /// Builds an instruction the named factories do not cover.
    /// </summary>
    /// <param name="messageClass">
    /// What the message is being declared to be, or <see langword="null"/> when the instruction only erases.
    /// Required as soon as <paramref name="set"/> includes <see cref="TellDatabases.Local"/>, because the
    /// daemon's own reading of a missing class there is to learn the message as legitimate - which is a
    /// wrong answer arrived at silently.
    /// </param>
    /// <param name="set">The databases to write the message to.</param>
    /// <param name="remove">The databases to erase the message from.</param>
    /// <returns>The instruction.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="set"/> and <paramref name="remove"/> are both <see cref="TellDatabases.None"/>, so the
    /// request would ask the daemon to do nothing; or <paramref name="set"/> includes
    /// <see cref="TellDatabases.Local"/> while <paramref name="messageClass"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="messageClass"/>, <paramref name="set"/> or <paramref name="remove"/> is not a value the
    /// enumeration declares.
    /// </exception>
    public static TellInstruction Create(MessageClass? messageClass, TellDatabases set, TellDatabases remove)
    {
        const TellDatabases DECLARED = TellDatabases.Local | TellDatabases.Remote;

        if (messageClass.HasValue && !Enum.IsDefined(messageClass.Value))
            throw new ArgumentOutOfRangeException(nameof(messageClass), messageClass, "Unable to create a tell instruction. The message class is not a declared value.");

        if ((set & ~DECLARED) != 0)
            throw new ArgumentOutOfRangeException(nameof(set), set, "Unable to create a tell instruction. The databases to set are not declared values.");

        if ((remove & ~DECLARED) != 0)
            throw new ArgumentOutOfRangeException(nameof(remove), remove, "Unable to create a tell instruction. The databases to remove are not declared values.");

        if (set == TellDatabases.None && remove == TellDatabases.None)
            throw new ArgumentException("Unable to create a tell instruction. Neither a database to set nor one to remove was named, so the request would ask the daemon to do nothing.", nameof(set));

        if (set.HasFlag(TellDatabases.Local) && messageClass is null)
            throw new ArgumentException("Unable to create a tell instruction. Writing to the local database declares what the message is, so a message class is required.", nameof(messageClass));

        return new(messageClass, set, remove);
    }
}
