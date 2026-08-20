# Josupeit.Mail.SpamAssassin.Client

A .NET client for **spamc/spamd** - the HTTP-like protocol SpamAssassin's scanning daemon speaks - so a mail pipeline written in C# can ask a local or remote `spamd` what it thinks of a message.

Hand over a message and get back what the command asked for: a spam verdict with score and threshold, the list of rules that hit, the human-readable report, or the rewritten message with SpamAssassin's markup in it. Bayes learning, forgetting, reporting and revoking are covered too, through the `TELL` command.

**The library owns no transport.** It never opens a socket or resolves a host: it takes a pair of streams - one to write the request to, one to read the response from - and speaks the protocol over them. Whether that is TCP to a remote host, a Unix domain socket to a local daemon, TLS through stunnel, or a `MemoryStream` in a test is the caller's decision.

## Status

The client is written and covered: all nine commands of protocol 1.5, each in a blocking and an awaited form, four ways of handing over a message, and optional zlib compression. 88 unit tests run against a fake daemon in about a tenth of a second; 17 integration tests run against a real `spamd` in a container. What is left is a first release.

[CLAUDE.md](CLAUDE.md) describes how the pieces fit together and collects the protocol details that are easy to get wrong - several of which are not in SpamAssassin's own protocol document and were read out of `spamd` itself.

## Packages

| Package                                                                                | Description                                                                      |
| -------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------- |
| [`Josupeit.Mail.SpamAssassin.Client`](src/Josupeit.Mail.SpamAssassin.Client/README.md) | The library. `ISpamAssassinClient` over any pair of streams, async and blocking. |

Targets `net8.0` and speaks protocol version **1.5**, which covers every command a current spamd implements.

## The sample

[`samples/Spamc`](samples/Spamc/README.md) is a working equivalent of SpamAssassin's own `spamc`, built on the library: it reads a message on stdin, asks `spamd` whatever the mode flag asked for, writes the answer on stdout, and reports the verdict in the exit code.

```bash
dotnet run --project samples/Spamc -- --help

# Is it spam? Prints "score/threshold" and exits 1 for spam, 0 for ham.
dotnet run --project samples/Spamc -- -c -d spamd.example.com < message.eml
```

It exists to show the parts a consumer has to supply - the socket, the exit codes, and what to do when the daemon is not there - and it is the shortest answer to "how do I use this". Its readme lists the `spamc` options it deliberately does not implement.

## Building and testing

```bash
# Build the entire solution
dotnet build Josupeit.Mail.SpamAssassin.Client.slnx

# Run the unit tests - no Docker needed
dotnet test test/Josupeit.Mail.SpamAssassin.Client.UnitTests

# Run everything, including the integration tests
dotnet test Josupeit.Mail.SpamAssassin.Client.slnx
```

The solution is in the `.slnx` format, which needs a recent SDK. `dotnet tool restore` once per clone installs `nbgv`, which the build uses to derive the version from the git history.

There are two test projects, and the split is deliberate. `Josupeit.Mail.SpamAssassin.Client.UnitTests` needs nothing but the SDK and covers the wire format byte-exactly, plus everything a correct spamd never sends. `Josupeit.Mail.SpamAssassin.Client.IntegrationTests` **requires Docker** - it builds an image with a real SpamAssassin, starts `spamd` in it, and asserts that the daemon agrees.

CI runs the unit tests only - the shared runners have no Docker daemon to lend - so the integration run is one you do locally before pushing.

## Contributing

[CONTRIBUTING.md](CONTRIBUTING.md) holds the code style and the commit-message rules; [CLAUDE.md](CLAUDE.md) describes what the project is and which design decisions are already settled.

Commits are stamped with a licence header by a hook that is not installed by default:

```bash
git config core.hooksPath .githooks
```

## Licence

[EUPL-1.2](LICENSE.md).
