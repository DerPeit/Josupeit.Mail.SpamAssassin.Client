// SPDX-License-Identifier: EUPL-1.2
// Copyright (c) Manuel Josupeit-Walter

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Josupeit.Mail.SpamAssassin.Client.Wire;

// Pure - spans in, values out - which is why both colours share all of it.
//
// Headers are decoded as Latin-1: single-byte by definition, and the one decoding that cannot fail on a byte the
// daemon should not have sent.
internal sealed class ResponseParser
{
    public ResponseHead ParseHead(ReadOnlySpan<byte> head)
    {
        var text = Encoding.Latin1.GetString(head).AsSpan();
        var status = TakeLine(ref text);

        var (version, code, responseText) = ParseStatusLine(status);

        long? contentLength = null;
        SpamVerdict? verdict = null;
        var didSet = TellDatabases.None;
        var didRemove = TellDatabases.None;

        while (!text.IsEmpty)
        {
            var line = TakeLine(ref text);

            if (line.IsWhiteSpace())
                continue;

            var colon = line.IndexOf(':');

            if (colon < 0)
                throw Malformed("a header without a colon", line);

            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            if (name.Equals("Content-length", StringComparison.OrdinalIgnoreCase))
                contentLength = ParseContentLength(value);
            else if (name.Equals("Spam", StringComparison.OrdinalIgnoreCase))
                verdict = ParseVerdict(value);
            else if (name.Equals("DidSet", StringComparison.OrdinalIgnoreCase))
                didSet = ParseDatabases(value);
            else if (name.Equals("DidRemove", StringComparison.OrdinalIgnoreCase))
                didRemove = ParseDatabases(value);
        }

        return new(version, code, responseText, contentLength, verdict, didSet, didRemove);
    }

    // Terminated with a line break or not depending on the daemon's age, which is what the trimming is for.
    public IReadOnlyList<string> ParseSymbols(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var symbols = new List<string>();
        var remaining = text.AsSpan();

        while (!remaining.IsEmpty)
        {
            var comma = remaining.IndexOf(',');
            var symbol = (comma < 0 ? remaining : remaining[..comma]).Trim();

            if (!symbol.IsEmpty)
                symbols.Add(symbol.ToString());

            if (comma < 0)
                break;

            remaining = remaining[(comma + 1)..];
        }

        return symbols;
    }

    private static (Version Version, SpamAssassinResponseCode Code, string ResponseText) ParseStatusLine(ReadOnlySpan<char> line)
    {
        const string PREFIX = "SPAMD/";

        if (!line.StartsWith(PREFIX, StringComparison.OrdinalIgnoreCase))
            throw Malformed($"a status line that does not start with \"{PREFIX}\"", line);

        var remaining = line[PREFIX.Length..];
        var space = remaining.IndexOf(' ');

        if (space < 0)
            throw Malformed("a status line with nothing after the version", line);

        if (!Version.TryParse(remaining[..space], out var version))
            throw Malformed("a status line whose version is not a version", line);

        remaining = remaining[(space + 1)..].TrimStart();
        space = remaining.IndexOf(' ');

        var codeText = space < 0 ? remaining : remaining[..space];

        if (!int.TryParse(codeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            throw Malformed("a status line whose response code is not a number", line);

        // Scrubbed here rather than where it is read: it reaches a caller both as ResponseHead.ResponseText and as the
        // message of an ErrorResponseSpamAssassinException, and only one of those two would have been remembered.
        var responseText = space < 0 ? string.Empty : Quote(remaining[(space + 1)..].Trim());

        return (version, (SpamAssassinResponseCode)code, responseText);
    }

    private static long ParseContentLength(ReadOnlySpan<char> value)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) || length < 0)
            throw Malformed("a content length that is not a length", value);

        return length;
    }

    // "True ; 15.0 / 5.0", or "Yes ; 15.0 / 5.0" from report-if-spam - the one command that spells the flag
    // differently, and nothing in the protocol document says so.
    private static SpamVerdict ParseVerdict(ReadOnlySpan<char> value)
    {
        var semicolon = value.IndexOf(';');

        if (semicolon < 0)
            throw Malformed("a spam header without a semicolon", value);

        var flag = value[..semicolon].Trim();

        var isSpam = flag.Equals("true", StringComparison.OrdinalIgnoreCase) || flag.Equals("yes", StringComparison.OrdinalIgnoreCase);

        if (!isSpam && !flag.Equals("false", StringComparison.OrdinalIgnoreCase) && !flag.Equals("no", StringComparison.OrdinalIgnoreCase))
            throw Malformed("a spam header whose verdict is neither true, false, yes nor no", flag);

        var scores = value[(semicolon + 1)..];
        var slash = scores.IndexOf('/');

        if (slash < 0)
            throw Malformed("a spam header without a threshold", value);

        return new(isSpam, ParseScore(scores[..slash], value), ParseScore(scores[(slash + 1)..], value));
    }

    private static double ParseScore(ReadOnlySpan<char> value, ReadOnlySpan<char> line)
    {
        // The daemon formats both with %2.1f, so only the invariant culture reads them back. NumberStyles.Float does
        // not keep TryParse from accepting "NaN" and "Infinity", and a score a caller cannot compare is no score.
        if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var score) || !double.IsFinite(score))
            throw Malformed("a spam header whose score is not a number", line);

        return score;
    }

    private static TellDatabases ParseDatabases(ReadOnlySpan<char> value)
    {
        var databases = TellDatabases.None;

        if (value.Contains("local", StringComparison.OrdinalIgnoreCase))
            databases |= TellDatabases.Local;

        if (value.Contains("remote", StringComparison.OrdinalIgnoreCase))
            databases |= TellDatabases.Remote;

        return databases;
    }

    private static ReadOnlySpan<char> TakeLine(ref ReadOnlySpan<char> text)
    {
        var end = text.IndexOf('\n');

        if (end < 0)
        {
            var whole = text;

            text = default;

            return whole.TrimEnd('\r');
        }

        var line = text[..end].TrimEnd('\r');

        text = text[(end + 1)..];

        return line;
    }

    private static MalformedResponseSpamAssassinException Malformed(string what, ReadOnlySpan<char> offending) =>
        new($"Unable to read the daemon's response. It sent {what} (\"{Quote(offending)}\").");

    // What the daemon sent ends up in an exception message, and that message ends up in a log or on a terminal.
    // Latin-1 decoding keeps every control byte intact, so a bare CR could forge a log line and an escape sequence
    // could drive the terminal. Escaping keeps the byte readable - which is the point of the message - without
    // letting it act, and the limit keeps a header block's worth of it out of the log.
    private static string Quote(ReadOnlySpan<char> text)
    {
        const int LIMIT = 200;

        var truncated = text.Length > LIMIT;
        var quoted = truncated ? text[..LIMIT] : text;
        var scrubbed = new StringBuilder(quoted.Length);

        foreach (var character in quoted)
        {
            if (IsPrintable(character))
                scrubbed.Append(character);
            else
                scrubbed.Append(CultureInfo.InvariantCulture, $"\\x{(int)character:x2}");
        }

        if (truncated)
            scrubbed.Append("...");

        return scrubbed.ToString();
    }

    // Latin-1 leaves both control ranges in play, and DEL between them.
    private static bool IsPrintable(char character) =>
        (character >= ' ' && character < (char)0x7f) || character > (char)0x9f;
}
