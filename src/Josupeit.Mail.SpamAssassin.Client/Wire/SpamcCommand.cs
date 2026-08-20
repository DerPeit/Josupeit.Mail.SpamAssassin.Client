// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

namespace Josupeit.Mail.SpamAssassin.Client.Wire;

// The commands of protocol 1.5. The wire spelling lives in RequestWriter, so this stays a name rather than a string
// with a number attached.
internal enum SpamcCommand
{
    Check,

    Symbols,

    Report,

    ReportIfSpam,

    Process,

    Headers,

    Tell,

    Ping,

    Skip,
}
