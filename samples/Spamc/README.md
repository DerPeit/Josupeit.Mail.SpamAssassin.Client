# spamc

A working equivalent of SpamAssassin's own `spamc`, built on `Josupeit.Mail.SpamAssassin.Client`. It reads a message on stdin, asks `spamd` whatever the mode flag asked for, writes the answer on stdout, and reports what happened in the exit code.

It is here to be read as much as to be run. Everything a consumer of the package has to supply itself is in this project and nowhere else - the socket, the exit codes, and the decision about what to do when the daemon is not there. The library supplies the protocol and nothing else.

## Running it

```pwsh
dotnet run --project samples/Spamc -- --help

# Is it spam? Prints "score/threshold" and exits 1 for spam, 0 for ham.
Get-Content message.eml | dotnet run --project samples/Spamc -- -c -d spamd.example.com

# The rewritten message, which is what spamc does when no mode flag is given.
Get-Content message.eml | dotnet run --project samples/Spamc > marked.eml

# Which rules hit, and the report that explains them.
Get-Content message.eml | dotnet run --project samples/Spamc -- -y
Get-Content message.eml | dotnet run --project samples/Spamc -- -R

# Bayes, over a Unix domain socket to a daemon started with --allow-tell.
Get-Content message.eml | dotnet run --project samples/Spamc -- -L spam -U /var/run/spamd.sock
```

The build produces `spamc.exe`, so it can also be dropped in where the real thing was:

```pwsh
dotnet build samples/Spamc -c Release
samples/Spamc/bin/Release/net8.0/spamc.exe -c < message.eml
```

## The modes

| Flag        | Command         | Writes                                               |
| ----------- | --------------- | ---------------------------------------------------- |
| _none_      | `PROCESS`       | The rewritten message.                               |
| `-c`        | `CHECK`         | `score/threshold`, and the verdict in the exit code. |
| `-y`        | `SYMBOLS`       | The names of the rules that hit.                     |
| `-R`        | `REPORT`        | `score/threshold`, then the report.                  |
| `-r`        | `REPORT_IFSPAM` | The same, and no report for a clean message.         |
| `--headers` | `HEADERS`       | The rewritten headers, then the original body.       |
| `-K`        | `PING`          | `SPAMD/1.5 0`.                                       |
| `-L`, `-C`  | `TELL`          | One line saying whether anything changed.            |

`-E` adds the verdict to the exit code for the modes that do not report it already. `SKIP` is the one command with no flag: nothing a command line can ask for opens a connection and then changes its mind.

One deliberate departure from the real thing: `-K` does not read stdin. `spamc` reads the message before it looks at the mode, so `spamc -K` at a terminal sits there waiting for input it will never send anywhere.

## The exit code

**0** means ham and **1** means spam - libspamc's own numbers, outside `sysexits.h` - and anything else is a `sysexits` code saying why there is no verdict. `SpamAssassinResponseCode` is what names them here, because the codes the daemon sends back and the codes `spamc` exits with are the same list.

A failure is not the end of it. By default a message the daemon could not be asked about is written out unchanged and the run reports success, on the grounds that a mail server which rejects everything while its scanner is down is worse than one that lets a few spams past. `-x` turns that off and is the flag for a caller that will queue and retry instead. `-c`, `-r` and `-R` print `0/0` on failure and `-y` a blank line, so that a script parsing the output still finds the shape it expects.

A message larger than `-s` is never sent: it is passed through and the run succeeds, whatever `-x` says.

## What it does not do

Everything left out is `spamc`'s own plumbing rather than anything about the protocol, and putting it in would make the sample longer without making it say more:

- **`--ssl` and the certificate options.** TLS is the transport's business, and the transport is the caller's - wrap the socket in an `SslStream` inside `SpamdConnection`, or put stunnel in front of the daemon.
- **`-H`, `--randomize`, `--connect-retries`, `--retry-sleep`, `--filter-retries`, `--filter-retry-sleep`.** Choosing among several daemons and retrying a failed one is a policy that belongs above the client, which is why the library takes a connection source and stays out of it.
- **`-B` (BSMTP), `-e` (pipe the result to a command), `-F` (a configuration file), `-n` (a separate connect timeout), `-X`, `-D`.**
- **Logging to syslog.** Failures always go to stderr here; `-l` is accepted and does nothing.

`-d` takes one host, not the comma-separated list the real `spamc` accepts, for the same reason as `-H`.

## The files

| File                       | What is in it                                                                   |
| -------------------------- | ------------------------------------------------------------------------------- |
| `Program.cs`               | The wiring: parse, build a client, run, and map a bad argument to an exit code. |
| `SpamdConnection.cs`       | The transport. Two streams over a socket, and shutting down the sending half.   |
| `SpamdConnectionSource.cs` | Where a connection comes from - TCP or a Unix domain socket, one per command.   |
| `SpamcRunner.cs`           | One command per mode, the output, the exit code, and the safe fallback.         |
| `SpamcCommandLine.cs`      | The flags, and the mapping from them onto `SpamcOptions`.                       |
| `SpamcOptions.cs`          | One invocation, as the command line described it.                               |

`System.CommandLine` is the only dependency this project has and the library does not: parsing a command line the way `getopt` does is not the library's business, and hand-rolling it here would be the least interesting code in the repository.
