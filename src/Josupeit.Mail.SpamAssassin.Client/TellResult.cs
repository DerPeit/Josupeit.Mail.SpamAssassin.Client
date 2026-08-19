// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// What a tell request actually changed, which is not necessarily what it asked for.
/// </summary>
/// <remarks>
/// The daemon reports the work it did and treats the rest as unremarkable: a database it was not configured
/// for, a Bayes store it has switched off, or a message it had nothing recorded about all produce a successful
/// response that changed less than the instruction named. Nothing here is an error, and no exception is
/// thrown for it - deciding whether the outcome was good enough is the caller's job, and this record is what
/// that decision is made on.
/// </remarks>
/// <param name="Set">The databases the message was actually written to.</param>
/// <param name="Removed">The databases the message was actually erased from.</param>
public sealed record TellResult(TellDatabases Set, TellDatabases Removed);
