// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// What SpamAssassin made of a message: whether it counts as spam, and the numbers behind that.
/// </summary>
/// <remarks>
/// Every scanning command answers with this much, so the verdict is part of every result rather than a
/// thing only <c>CHECK</c> produces.
/// </remarks>
/// <param name="IsSpam">
/// <see langword="true"/> when <paramref name="Score"/> reached <paramref name="Threshold"/> and the
/// daemon therefore called the message spam. It is the daemon's own answer, not a comparison performed
/// here, because a per-user configuration may reach a different conclusion than the two numbers suggest.
/// </param>
/// <param name="Score">
/// The score the rules added up to. Higher is worse, and it can be negative when a message hit rules that
/// argue for it.
/// <para>
/// A quirk worth knowing before comparing this to <paramref name="Threshold"/> yourself: a message that is
/// <em>not</em> spam and whose score landed exactly on the threshold is reported one tenth below it
/// instead, so that a caller doing its own comparison cannot arrive at "spam" for a message the daemon
/// cleared.
/// </para>
/// </param>
/// <param name="Threshold">
/// The score at which this daemon - for this user, if the request named one - starts calling a message
/// spam. It is configuration rather than a constant, so it is reported per message and may differ between
/// two scans.
/// </param>
public readonly record struct SpamVerdict(bool IsSpam, double Score, double Threshold);
