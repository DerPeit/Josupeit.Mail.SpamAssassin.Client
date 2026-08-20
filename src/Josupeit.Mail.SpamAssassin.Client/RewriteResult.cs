// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// The answer to a request that rewrites the message: the verdict, and how much was written to the
/// destination stream the caller supplied.
/// </summary>
/// <remarks>
/// The rewritten message itself is not here. It went into the destination stream as it arrived, which is the
/// whole point: a message can be larger than anything worth holding in memory, and the caller already has
/// somewhere to put it.
/// </remarks>
/// <param name="Verdict">Whether the message counts as spam, with the score and the threshold.</param>
/// <param name="Length">
/// The number of bytes written to the destination stream. This is what the daemon announced <em>and</em>
/// what was actually copied - the two disagreeing is a protocol error rather than something reported here.
/// </param>
public sealed record RewriteResult(SpamVerdict Verdict, long Length);
