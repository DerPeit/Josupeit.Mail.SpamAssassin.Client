// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using Josupeit.Mail.SpamAssassin.Client;

namespace Spamc;

// One invocation of this sample, as the command line described it. SpamcCommandLine builds it and nothing else
// changes it, so everything downstream can read the decisions rather than re-derive them from the arguments.
//
// The defaults are spamc's own, including the ones that look arbitrary: 783 is the daemon's registered port, 500
// kibibytes is the size past which spamc stops asking and just passes the mail on, and ten minutes is how long it
// is prepared to wait for an answer.
internal sealed record SpamcOptions
{
    public SpamcMode Mode { get; init; }

    public string Host { get; init; } = "localhost";

    public int Port { get; init; } = 783;

    // -U. Set, it wins over Host and Port: a daemon on the same machine is reached over a Unix domain socket
    // rather than through the loopback interface.
    public string? SocketPath { get; init; }

    public string? User { get; init; }

    public int MaxMessageLength { get; init; } = 500 * 1024;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

    public bool Compress { get; init; }

    // On unless -x was given, and it is the flag that decides what a broken daemon costs: with it, a message the
    // daemon could not be asked about is passed through unchanged and the mail keeps moving.
    public bool SafeFallback { get; init; } = true;

    // -E. Whether a filtering run reports the verdict in its exit code as well as in its output. -c does that
    // regardless, which is why this is not simply "what the exit code says".
    public bool UseExitCode { get; init; }

    // What -L or -C asked for, and null for every mode that is not a tell. The five combinations the library
    // names are exactly the five spamc's two flags can produce.
    public TellInstruction? Instruction { get; init; }
}
