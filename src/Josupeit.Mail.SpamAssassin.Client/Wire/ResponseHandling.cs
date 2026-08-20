// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

namespace Josupeit.Mail.SpamAssassin.Client.Wire;

// How much of a response a command expects, which is all that differs between the exchanges once the request has
// gone out.
internal enum ResponseHandling
{
    // A skip is answered by a closed connection, and reading would turn that into a complaint.
    None,

    Head,

    // Held in memory and read as text: a report, or a list of rule names.
    Text,

    // Copied straight into the stream the caller supplied.
    Body,
}
