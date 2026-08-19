// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System.CommandLine;
using Josupeit.Mail.SpamAssassin.Client;

namespace Spamc;

// spamc's command line, as far as this sample copies it: the flags that are about the protocol, and none of the
// ones that are about spamc's own plumbing. README.md lists what was left out and why.
//
// The options are fields because reading a parse result needs the very same instances that were added to the
// command. That keeps the two halves next to each other - the shape of the command line, and the mapping from it
// onto SpamcOptions - which is the only way they stay in step.
internal sealed class SpamcCommandLine
{
    private static readonly string[] _learnTypes = ["spam", "ham", "forget"];
    private static readonly string[] _reportTypes = ["report", "revoke"];

    private readonly Option<string> _dest = new("--dest", "-d")
    {
        Description = "The host running spamd.",
        DefaultValueFactory = _ => "localhost",
    };

    private readonly Option<int> _port = new("--port", "-p")
    {
        Description = "The port spamd listens on.",
        DefaultValueFactory = _ => 783,
    };

    private readonly Option<string> _socket = new("--socket", "-U")
    {
        Description = "The path of a Unix domain socket to reach spamd through, instead of a host and port.",
    };

    private readonly Option<string> _username = new("--username", "-u")
    {
        Description = "The user whose configuration spamd should scan with.",
    };

    private readonly Option<int> _maxSize = new("--max-size", "-s")
    {
        Description = "The largest message to send, in bytes. A larger one is passed through unscanned.",
        DefaultValueFactory = _ => 500 * 1024,
    };

    private readonly Option<int> _timeout = new("--timeout", "-t")
    {
        Description = "How long to wait for the whole exchange, in seconds.",
        DefaultValueFactory = _ => 600,
    };

    private readonly Option<string> _learnType = new("--learntype", "-L")
    {
        Description = "Teach spamd about the message: spam, ham, or forget to undo either.",
        HelpName = "spam|ham|forget",
    };

    private readonly Option<string> _reportType = new("--reporttype", "-C")
    {
        Description = "Tell the collaborative networks about the message: report it as spam, or revoke a report.",
        HelpName = "report|revoke",
    };

    private readonly Option<bool> _check = new("--check", "-c")
    {
        Description = "Print the summary line only, and report the verdict in the exit code.",
    };

    private readonly Option<bool> _tests = new("--tests", "-y")
    {
        Description = "Print the names of the rules that hit, and nothing else.",
    };

    private readonly Option<bool> _fullSpam = new("--full-spam", "-r")
    {
        Description = "Print the full report, but only for a message that is spam.",
    };

    private readonly Option<bool> _full = new("--full", "-R")
    {
        Description = "Print the full report for every message.",
    };

    private readonly Option<bool> _headers = new("--headers")
    {
        Description = "Rewrite the headers only, and put the original body back behind them.",
    };

    private readonly Option<bool> _ping = new("-K")
    {
        Description = "Ask whether spamd is alive, and scan nothing.",
    };

    private readonly Option<bool> _exitCode = new("--exitcode", "-E")
    {
        Description = "Filter as usual, and report the verdict in the exit code as well.",
    };

    private readonly Option<bool> _noSafeFallback = new("--no-safe-fallback", "-x")
    {
        Description = "On failure, report the error instead of passing the message through unchanged.",
    };

    private readonly Option<bool> _compress = new("--compress", "-z")
    {
        Description = "Compress the message on the way to spamd, which spamd has to be configured to allow.",
    };

    private readonly Option<bool> _logToStderr = new("--log-to-stderr", "-l")
    {
        Description = "Accepted for compatibility. This sample always reports failures on stderr.",
    };

    private readonly (Option<bool> Flag, SpamcMode Mode)[] _modeFlags;

    public SpamcCommandLine(Func<SpamcOptions, CancellationToken, Task<int>> run)
    {
        // One table rather than two chains of ifs: which mode a flag selects and the check for more than one mode
        // being given then cannot disagree.
        _modeFlags =
        [
            (_check, SpamcMode.Check),
            (_tests, SpamcMode.Symbols),
            (_fullSpam, SpamcMode.ReportIfSpam),
            (_full, SpamcMode.Report),
            (_headers, SpamcMode.Headers),
            (_ping, SpamcMode.Ping),
        ];

        Root = new("A spamc-equivalent client for SpamAssassin's spamd. Reads a message on stdin, writes what the mode asked for on stdout, and reports the verdict in the exit code.");

        Root.Options.Add(_dest);
        Root.Options.Add(_port);
        Root.Options.Add(_socket);
        Root.Options.Add(_username);
        Root.Options.Add(_maxSize);
        Root.Options.Add(_timeout);
        Root.Options.Add(_learnType);
        Root.Options.Add(_reportType);
        Root.Options.Add(_check);
        Root.Options.Add(_tests);
        Root.Options.Add(_fullSpam);
        Root.Options.Add(_full);
        Root.Options.Add(_headers);
        Root.Options.Add(_ping);
        Root.Options.Add(_exitCode);
        Root.Options.Add(_noSafeFallback);
        Root.Options.Add(_compress);
        Root.Options.Add(_logToStderr);

        Root.SetAction((parsed, cancellationToken) => run(Read(parsed), cancellationToken));
    }

    public RootCommand Root { get; }

    // The parser's own complaints plus the ones that are about a combination rather than a single flag. Both are
    // usage errors and both cost the same exit code, so the caller does not have to tell them apart.
    public IReadOnlyList<string> Validate(ParseResult parsed)
    {
        var errors = parsed.Errors.Select(error => error.Message).ToList();
        var modes = GivenModes(parsed);

        // spamc lets the last mode flag win, which quietly does something other than what a script that grew a
        // second flag by accident is asking for.
        if (modes.Count > 1)
            errors.Add($"Only one mode may be given, and these were: {string.Join(", ", modes)}.");

        AddUnknownValue(errors, parsed, _learnType, _learnTypes);
        AddUnknownValue(errors, parsed, _reportType, _reportTypes);

        return errors;
    }

    private static void AddUnknownValue(List<string> errors, ParseResult parsed, Option<string> option, string[] allowed)
    {
        var value = parsed.GetValue(option);

        if (value is not null && !allowed.Contains(value))
            errors.Add($"{option.Name} must be one of {string.Join(", ", allowed)}, not '{value}'.");
    }

    private SpamcOptions Read(ParseResult parsed) => new()
    {
        Mode = ReadMode(parsed),
        Host = parsed.GetValue(_dest) ?? "localhost",
        Port = parsed.GetValue(_port),
        SocketPath = parsed.GetValue(_socket),
        User = parsed.GetValue(_username),
        MaxMessageLength = parsed.GetValue(_maxSize),
        Timeout = TimeSpan.FromSeconds(parsed.GetValue(_timeout)),
        Compress = parsed.GetValue(_compress),
        SafeFallback = !parsed.GetValue(_noSafeFallback),
        UseExitCode = parsed.GetValue(_exitCode),
        Instruction = ReadInstruction(parsed),
    };

    private SpamcMode ReadMode(ParseResult parsed)
    {
        foreach (var (flag, mode) in _modeFlags)
        {
            if (parsed.GetValue(flag))
                return mode;
        }

        if (parsed.GetValue(_learnType) is not null)
            return SpamcMode.Learn;

        if (parsed.GetValue(_reportType) is not null)
            return SpamcMode.ReportMessage;

        return SpamcMode.Process;
    }

    // -L and -C are the same TELL command with different headers, which is why the library names the five
    // combinations instead of offering a learn method and a report method.
    private TellInstruction? ReadInstruction(ParseResult parsed) => (parsed.GetValue(_learnType), parsed.GetValue(_reportType)) switch
    {
        ("spam", _) => TellInstruction.LearnAsSpam,
        ("ham", _) => TellInstruction.LearnAsHam,
        ("forget", _) => TellInstruction.Forget,
        (_, "report") => TellInstruction.ReportAsSpam,
        (_, "revoke") => TellInstruction.RevokeAsHam,
        _ => null,
    };

    private IReadOnlyList<string> GivenModes(ParseResult parsed)
    {
        var given = _modeFlags.Where(entry => parsed.GetValue(entry.Flag)).Select(entry => entry.Flag.Name).ToList();

        if (parsed.GetValue(_learnType) is not null)
            given.Add(_learnType.Name);

        if (parsed.GetValue(_reportType) is not null)
            given.Add(_reportType.Name);

        return given;
    }
}
