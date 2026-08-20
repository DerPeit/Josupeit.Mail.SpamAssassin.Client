// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using Josupeit.Mail.SpamAssassin.Client;
using Spamc;

// spamc, as much of it as this library can be shown off with: read a message on stdin, ask spamd about it, write
// the answer on stdout, and say in the exit code what happened.
//
// It is here to be read as much as run. Everything a consumer of the package has to supply itself is in this
// project and nowhere else - the socket, the exit codes, the decision about what to do when the daemon is not
// there - and the library supplies the protocol.

var commandLine = new SpamcCommandLine(RunAsync);
var parsed = commandLine.Root.Parse(args);
var errors = commandLine.Validate(parsed);

if (errors.Count > 0)
{
    foreach (var error in errors)
        await Console.Error.WriteLineAsync($"spamc: {error}");

    // The parser's own exit code for this is 1, which here would mean "the message is spam".
    return (int)SpamAssassinResponseCode.Usage;
}

// --help and --version are actions System.CommandLine supplies; anything else reaches RunAsync.
return await parsed.InvokeAsync();

static async Task<int> RunAsync(SpamcOptions options, CancellationToken cancellationToken)
{
    // Both of these as streams rather than through Console.In and Console.Out: a message is bytes, and a console
    // writer would re-encode them and translate the line endings on the way past. Disposed asynchronously because
    // disposing stdout flushes it, and flushing is the one part of this that can block.
    await using var input = Console.OpenStandardInput();
    await using var output = Console.OpenStandardOutput();

    // A token source is only IDisposable - there is nothing asynchronous about letting a timer go.
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    deadline.CancelAfter(options.Timeout);

    var connections = new SpamdConnectionSource(options);

    SpamAssassinClient client;

    try
    {
        client = new(connections, new() { User = options.User, CompressRequests = options.Compress });
    }
    catch (ArgumentException invalid)
    {
        // The options record checks the user name against what the daemon accepts, which turns a bad -u into a
        // usage error here rather than a response code from the far end.
        await Console.Error.WriteLineAsync($"spamc: {invalid.Message}");

        return (int)SpamAssassinResponseCode.Usage;
    }

    // One client instance would serve any number of messages; this process only ever has the one on stdin.
    var runner = new SpamcRunner(options, client, input, output);

    return await runner.RunAsync(deadline.Token);
}
