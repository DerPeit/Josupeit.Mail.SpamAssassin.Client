// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

namespace Spamc;

// Which of the daemon's commands an invocation asked for. spamc decides that with a mode flag, and the flag is
// named against each member here: the name spells out the intent, the comment keeps the mnemonic findable.
internal enum SpamcMode
{
    // No mode flag at all - PROCESS, and the rewritten message goes to stdout. spamc's default, and the only
    // mode whose output is a message rather than a line about one.
    Process,

    // -c, --check
    Check,

    // -y, --tests
    Symbols,

    // -R, --full
    Report,

    // -r, --full-spam
    ReportIfSpam,

    // --headers
    Headers,

    // -K
    Ping,

    // -L, --learntype
    Learn,

    // -C, --reporttype
    ReportMessage,
}
