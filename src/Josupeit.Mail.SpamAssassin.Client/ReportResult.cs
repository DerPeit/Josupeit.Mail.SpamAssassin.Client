// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

namespace Josupeit.Mail.SpamAssassin.Client;

/// <summary>
/// The answer to a report request: the verdict, plus the report SpamAssassin wrote about the message.
/// </summary>
/// <param name="Verdict">Whether the message counts as spam, with the score and the threshold.</param>
/// <param name="Report">
/// The report as the daemon produced it: prose for a human, listing the rules that hit and what each cost.
/// <para>
/// <see langword="null"/> only for a report-if-spam request that found the message clean - the daemon sends
/// no report at all in that case, which is a different thing from sending an empty one. Whitespace at the
/// end is not to be relied upon in either direction: whether the report ends in a line break, and how many,
/// differs between daemon versions.
/// </para>
/// </param>
public sealed record ReportResult(SpamVerdict Verdict, string? Report);
