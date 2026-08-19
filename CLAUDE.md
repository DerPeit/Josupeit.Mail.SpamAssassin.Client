# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**Read [CONTRIBUTING.md](CONTRIBUTING.md) before writing code or committing.** It holds the code style (layout, naming, statement grouping, async, performance mindset, tests) and the commit-message rules. This file covers what the project is, how the protocol works, and which design decisions are already settled.

## What this is

A .NET library that speaks **spamc/spamd**, the HTTP-like request/response protocol of SpamAssassin's scanning daemon. A caller hands over a message; the library writes a spamc request, reads the response, and returns the verdict, the symbols, the report or the rewritten message - whatever the command asked for.

**The library owns no transport.** It never opens a socket, resolves a host or knows whether spamd is local or remote: it is handed a pair of streams - one to write the request to, one to read the response from - and speaks the protocol over them. TCP, a Unix domain socket, an stunnel-wrapped TLS connection or a `MemoryStream` in a test are all the same to it. That is a deliberate decision rather than an omission: the daemon may be anywhere, reached any number of ways, and a library that owned the socket would have to have an opinion about all of them.

**Where it stands:** complete and covered on both sides. `test/…UnitTests` is 88 tests over a fake daemon, `test/…IntegrationTests` is 17 against a real `spamd` in a container, and both pass. What is left is a first release.

## Build & test

```pwsh
dotnet build Josupeit.Mail.SpamAssassin.Client.slnx   # solution is .slnx (XML format), needs a recent SDK
dotnet test  Josupeit.Mail.SpamAssassin.Client.slnx
dotnet test  test/Josupeit.Mail.SpamAssassin.Client.UnitTests   # the Docker-free half, seconds instead of minutes
dotnet test --filter "Name~Scan_reports_spam"                   # single test by name
dotnet tool restore                                             # once per clone; nbgv is a local tool
```

**The integration tests require Docker**, and cost about two minutes: `SpamdFixture` builds an Ubuntu image carrying SpamAssassin (`Dockerfile.spamd`) and runs a real `spamd` for the length of the fixture - one container for the class, because starting the daemon means compiling the stock rule set. It starts with `--allow-tell`, without which every TELL is refused, and `--local`, which keeps the scan off the network so a score is stable enough to assert on; `local.cf` pins the rest of the scoring. Both the container and the built image are deleted on teardown, so a run leaves nothing behind - and pays for the image again next time.

`TcpSpamdConnection` in that project is the transport this library deliberately does not ship, written the way a consumer would write it, which makes those tests a check on the connection abstraction as much as on the wire. **It is the same implementation the package readme shows; change one and change the other.**

`Josupeit.Mail.SpamAssassin.Client.UnitTests` is Docker-free and covers what a correct spamd never sends: truncated responses, a missing `Content-length`, a body shorter than the one announced, junk in the status line.

Two things about the integration suite that look like flakiness and are not. The **Bayes tests learn and then forget** the message they used, which is what keeps them from leaving state for the tests after them - and learning the same message twice legitimately changes nothing the second time, which is itself a test. And a daemon that cannot write its Bayes database answers **successfully** while changing nothing, so `A_message_can_be_learned_and_forgotten_again` prints spamd's own log on failure; that log is the only place the reason appears.

**CI runs the unit tests only.** Codeberg's shared runners are container jobs with no Docker daemon of their own, so the integration half cannot run there - it is a local step before pushing, not a gate. `.forgejo/workflows/ci.yml` is the whole pipeline.

**GTUBE** is the way to make a scan come back spam without inventing a plausible-looking spam message: the 68-character test pattern documented at <https://spamassassin.apache.org/gtube/> scores 1000 by rule. Use it for the "is spam" cases and any dull ham for the others.

## Projects

The tree is split two ways, and the split decides what a project inherits. `src/Directory.Build.props` carries everything to do with shipping a package, so anything under `src/` is packable unless it opts out; `test/Directory.Build.props` carries the test-runner packages and the reference to the library, so the test projects are nearly empty files. Both import the repository-wide `Directory.Build.props` by hand, because MSBuild reads only the nearest one.

| Project                                                   | TFM    | Role                                                                                   |
| --------------------------------------------------------- | ------ | -------------------------------------------------------------------------------------- |
| `src/Josupeit.Mail.SpamAssassin.Client`                   | net8.0 | The library, and the only package. Namespace root `Josupeit.Mail.SpamAssassin.Client`. |
| `test/Josupeit.Mail.SpamAssassin.Client.UnitTests`        | net8.0 | NUnit tests over the wire format and the parser. No Docker.                            |
| `test/Josupeit.Mail.SpamAssassin.Client.IntegrationTests` | net8.0 | NUnit + Testcontainers tests against a real spamd. Needs Docker.                       |

There is no samples project and no source generator: nothing here is generated, and nothing about the wire format would be better written by a generator than by hand.

## The agreed design

These were decided before any code was written. Departing from one is a decision to make out loud, not a detail to improvise.

- **`ISpamAssassinClient` and `SpamAssassinClient`.** One interface, one implementation, **one method per use case** - `ScanAsync`, `SymbolsAsync`, `ReportAsync`, `ReportIfSpamAsync`, `ProcessAsync`, `HeadersAsync`, `TellAsync`, `PingAsync`, `SkipAsync`, or whatever the domain names for those turn out to be. No static methods, no command enum threaded through a single `SendAsync`, and no builder.
- **Async and genuinely synchronous.** Every method has a real blocking overload built on `Stream.Read`/`Stream.Write` - never `GetAwaiter().GetResult()` over the async path. The protocol logic is written once over a seam that has both a sync and an async side rather than copied twice; what that seam looks like is the first implementation decision to make.
- **The stream pair comes from a factory passed to the constructor.** spamd serves exactly one command per connection and then closes it, so a client with many methods cannot hold one connection open. The constructor takes a factory - a `Func<CancellationToken, Task<...>>` returning the connection, plus a `Func<...>` for the blocking half, which takes no token - and each call obtains a fresh pair and disposes it when the exchange is over. That keeps the socket out of the library while still letting one client instance serve many scans.
- **The options record is a constructor argument**: an immutable record with `init` accessors that validate. It carries the default `User`, whether to offer `Compress: zlib`, the protocol version to announce, and the bounds on what a response may make the client allocate.
- **Message input has four overloads.** `Stream` with a `CanSeek` precondition, so the length is read rather than guessed; `(Stream, long length)` for a stream whose length the caller knows, which is never buffered; `ReadOnlySequence<byte>`; and `ReadOnlyMemory<byte>`. The reason the plain `Stream` overload demands `CanSeek` is `Content-length`: spamd wants the size before the body, so a non-seekable stream would have to be buffered whole, and a caller who wants that should say so by measuring it themselves.
- **A rewritten message is copied into a destination stream the caller supplies.** `PROCESS` and `HEADERS` therefore take a destination and return only the status and the headers. Nothing hands back a lazily-drained stream over a connection the client is about to dispose.
- **All response headers are parsed**, not only the ones a given command needs. What spamd actually emits, verified in `spamd/spamd.raw` rather than taken from the protocol document: `Content-length`, `Spam`, `DidSet` and `DidRemove`, and nothing else. There are **no** `Didn't-Set`/`Didn't-Remove` headers - a database that was not changed is simply absent from `DidSet`/`DidRemove`. An unknown header is ignored rather than treated as an error; the protocol says so explicitly, and it is how a future header stays non-breaking.
- **XML doc comments on the public API surface, and nowhere else.** See CONTRIBUTING.md for what makes one complete.
- **No `Microsoft.Extensions.*` dependency.** No DI registration, no `ILogger`, no `IOptions`. A consumer wires those up around the library.
- **Every failure has a named exception.** `SpamAssassinException` is abstract; what is actually thrown is `ErrorResponseSpamAssassinException` (the daemon refused, and its code is on the exception), `MalformedResponseSpamAssassinException` (not a response at all), `TruncatedResponseSpamAssassinException` (stopped mid-answer, with how much arrived) or `ResponseTooLargeSpamAssassinException` (passed a configured limit, with the limit). Only the last two are worth a retry, which is the reason they are told apart at all.
- **One runtime dependency: `Microsoft.IO.RecyclableMemoryStream`.** The two places a response or a compressed request has to be held are pooled through it rather than through `MemoryStream`. One `RecyclableMemoryStreamManager` per client instance, handed to the wire types in their constructors - there is no static anywhere in this library.

## How the code is laid out

`Wire/` holds the protocol and is `internal` throughout: nothing about the wire format is a public extension point, so nothing about it is public. Inside those types the members are `public` - the type is the boundary, and an `internal` member of an `internal` class says nothing the class did not already say.

- **`MessageBody`** - the four ways of handing over a message reduced to a length plus somewhere to read from, and the one place the public argument validation lives (hence its `"message"` parameter name).
- **`HeadWriter`** - a pooled ASCII buffer for the request head. `RequestWriter` builds the head into it and never touches a stream while doing so, which is what makes the two colours produce byte-identical requests.
- **`RequestWriter`** - takes its invariants in the constructor (options, head writer, transfer buffer, stream manager) and only what varies per call in its methods. Owns compression, including the awkward part: `Content-length` has to be the compressed length, so a compressed request is buffered whole.
- **`ResponseParser`** - pure. Bytes in, `ResponseHead` out; it is what both colours share so they cannot disagree about what a response means. Headers are decoded as Latin-1, which cannot fail on a byte the daemon should not have sent.
- **`ResponseReader`** - one pooled buffer for both halves of a response, because the read that finds the end of the header block usually drags the first bytes of the body with it.
- **`SpamAssassinClient`** - sixty public methods that are each one line over fourteen private cores, over one `Exchange`/`ExchangeAsync` pair. The duplication between the colours is larger than it looks: seven pumping loops (`WriteBody`, `Compress`, `ReadHead`, `CopyBody`, `ReadText`, `Fill`, and the transfer inside `Exchange`) **plus the `Exchange`/`ExchangeAsync` orchestration itself**, which is written out twice. That is the price of a genuine sync twin and it is paid on purpose - but nothing makes the two copies disagree loudly, so **every change to one is a change to the other, diffed side by side**. They are kept in the same shape down to the switch form for exactly that reason.

## The protocol

The authority is `spamd/PROTOCOL` in <https://github.com/apache/spamassassin> - read it there rather than trusting a summary, including this one. `spamc/libspamc.c` is where the reference client's actual behaviour lives when the document and reality disagree.

A request is a command line, headers, a blank line and the message:

```
CHECK SPAMC/1.5\r\n
Content-length: 1234\r\n
User: alice\r\n
\r\n
<message>
```

A response is a status line, headers, a blank line and - for some commands - a body:

```
SPAMD/1.1 0 EX_OK\r\n
Spam: True ; 15 / 5\r\n
\r\n
```

Everything is CRLF-delimited, and after each side has finished writing it shuts down its half of the connection. The status line carries a protocol version, a response code from `sysexits.h` (`0` = `EX_OK`, `65` = `EX_DATAERR`, `70` = `EX_SOFTWARE`, `76` = `EX_PROTOCOL`, and the rest), and a message describing the error if there was one. **A non-zero code means no body follows** and the connection closes after that line.

| Command         | Answers with                                                                             |
| --------------- | ---------------------------------------------------------------------------------------- |
| `CHECK`         | A `Spam:` header only: `True`/`False`, then `;`, then the score, `/`, and the threshold. |
| `SYMBOLS`       | As `CHECK`, plus a comma-separated list of the rules that hit, as the body.              |
| `REPORT`        | As `CHECK`, plus the human-readable report as the body.                                  |
| `REPORT_IFSPAM` | As `REPORT` when the message is spam, and **nothing at all** when it is not.             |
| `PROCESS`       | The whole rewritten message as the body.                                                 |
| `HEADERS`       | As `PROCESS`, but stopping after the header/body separator. Needs protocol 1.4.          |
| `TELL`          | `DidSet:`/`DidRemove:` headers saying what it actually did. Needs spamd `--allow-tell`.  |
| `PING`          | `SPAMD/1.5 0 PONG` and nothing else.                                                     |
| `SKIP`          | Nothing: the client opened the connection and changed its mind.                          |

Details that are easy to get wrong. The first four are not in `PROTOCOL` at all and come from reading `spamd/spamd.raw`:

- **The `Spam:` header comes back from every scanning command**, not only `CHECK` - `SYMBOLS`, `REPORT`, `REPORT_IFSPAM`, `PROCESS` and `HEADERS` all carry it, as long as the request announced protocol 1.3 or later. That is why a verdict is part of every result type here rather than of one.
- **`REPORT_IFSPAM` writes `Yes`/`No` in that header, where every other command writes `True`/`False`.** All four have to be accepted, case-insensitively. The reference client only ever compares against `true`, which is why nothing upstream trips over it.
- **The score is formatted `%2.1f`** and has to be parsed with the invariant culture. A message that is _not_ spam and whose score landed exactly on the threshold is reported as `threshold - 0.1` instead, so that a client doing its own comparison cannot conclude "spam" about a message spamd cleared.
- **A `TELL` response ends with an extra blank line**: spamd writes the headers and then `\r\n\r\n`, so the header block is terminated twice over. Be lenient, and do not assert on the exact tail.
- **`SYMBOLS` and `REPORT` bodies are not reliably CRLF-terminated.** Some versions end the line, some do not, and the number of trailing CRLFs varies. Be flexible reading them, and do not assert a particular tail in a test.
- **`PING` still needs the blank line.** Since protocol 1.5 a client sends the command, no headers, then the empty line, for consistency with every other command.
- **`TELL` is driven by three request headers** - `Message-class: spam|ham`, `Set: local[, remote]`, `Remove: local[, remote]` - and learning, forgetting, reporting and revoking are all combinations of those rather than separate commands. Learn as spam is `Message-class: spam` + `Set: local`; forget is `Remove: local`; report is `Message-class: spam` + `Set: local, remote`; revoke is `Message-class: ham` + `Set: local` + `Remove: remote`.
- **It is up to the caller to decide whether the right thing happened**: spamd only reports what it did, in `DidSet`/`DidRemove`, and a `TELL` that changed nothing is not an error.
- **`Compress: zlib`** applies to the request body only, and only when spamd allows compression. Off unless the options record turns it on. **`Content-length` is then the compressed length**, which spamd insists on having (`Compress requires Content-length header`) - so compressing a request means buffering it to learn that length, and the "never buffers" promise of the explicit-length overload holds only while compression is off.
- **`Content-length` is effectively mandatory** from protocol 1.2 onwards, in both directions.
- The reference client caps a message at 256 MiB (`SPAMC_MAX_MESSAGE_LEN`) and answers `EX_TOOBIG` (866) past it. That number is client-side policy, not a wire rule.
- Headers may repeat, and that has been defined behaviour only since protocol 1.3.

## Packaging

`src/Josupeit.Mail.SpamAssassin.Client` is the only package, and it is packable by virtue of sitting under `src/`.

- **Versions come from the git history**, via Nerdbank.GitVersioning: `version.json` at the root sets the base version, `src/Josupeit.Mail.SpamAssassin.Client/version.json` sets the release tag pattern (`v{version}`). Nothing hand-edits a version number, and a shallow clone gets it wrong - which is why the CI checkout is a plain full `git clone`.
- **Release notes come from the git log.** `GeneratePackageReleaseNotes` in `src/Directory.Build.targets` runs `git log --pretty=format:"- %s"` over the commits touching the project since the previous release tag. That is the practical reason the Conventional Commits rule in CONTRIBUTING.md matters: those subjects are read by consumers.
- **`GenerateDocumentationFile` is on**, so CS1591/CS1573 police the doc comments on the public surface. Neither is suppressed, and both are expected to stay at zero.
- **SourceLink is wired by hand**, in `src/Directory.Build.props`: the SDK contributes a provider for each host it knows and Gitea is not one of them, so `Microsoft.SourceLink.Gitea` plus a `SourceLinkGiteaHost` item name codeberg.org. Removing either fails nothing - the package quietly loses source stepping.
- The package carries an SBOM, a `snupkg` with embedded sources, `LICENSE.md` and the project's own `README.md`.
- **There is no icon yet.** `assets/icon.png` does not exist, and both the `PackageIcon` property and the `None` item that packs it sit behind an `Exists` condition, so a pack succeeds without one. Drop a 256x256 PNG there and it is picked up with no further change - NuGet caps a package icon at 1 MB.

Verify a packaging change by unzipping the result rather than by trusting a green `dotnet pack`: `dotnet pack src/Josupeit.Mail.SpamAssassin.Client -c Release -o packages` should produce `lib/net8.0/` holding the library and its `.xml` doc file **and nothing else**, plus `README.md`, `LICENSE.md`, an `_manifest/`, and no package dependencies at all.

## Branching & releasing

`develop` is the default branch and where feature branches land; `main` is the release branch. **Merge with `--no-ff` in both directions**, so a feature keeps its own commits and the merge commit stays a thing a release can be rewritten onto.

**Every merge commit carries a written message, never the default.** `Merge branch 'x' into y` repeats what the graph already shows. Say what the branch was for and why it was worth doing - and more so for a merge into `main`, whose message is the nearest thing to a release description the history holds. Pass it with `git merge -F <file>`: `git merge` does not read a message from stdin, so `-F -` fails.

`main` is not merely a convention: `version.json` sets `publicReleaseRefSpec` to `^refs/heads/main$`, so only a build off `main` produces a clean version - anywhere else Nerdbank.GitVersioning appends the commit id. The Pack and Push steps in CI are gated on the same ref, so **a push to `main` publishes**, as far as the pipeline is concerned; the only thing standing between a merge and nuget.org is the `NUGET_API_KEY` secret existing on the repository.

After merging `develop` into `main`:

1. Dry-run the tag to see the version it would carry:

   ```pwsh
   dotnet nbgv tag --what-if -p src/Josupeit.Mail.SpamAssassin.Client
   ```

2. Reword the merge commit to the release convention - major.minor, no package name, because there is only one package here:

   ```pwsh
   git commit --amend -m "chore(release): 0.1"
   ```

3. Create the tag for real:

   ```pwsh
   dotnet nbgv tag -p src/Josupeit.Mail.SpamAssassin.Client
   ```

The tag does more than name the version: `GeneratePackageReleaseNotes` finds the previous release with `git describe --match "v*"`, so skipping it makes the next release repeat the whole history.

### nbgv notes

- It is a **local dotnet tool**: `dotnet nbgv`, never bare `nbgv`, and `dotnet tool restore` has to have been run once in the clone.
- `-p` takes the **project directory**, not the `.csproj`.
- `--what-if` is the dry run for the `tag` subcommand.

## Conventions & gotchas

- Internals are exposed to both test projects through `<InternalsVisibleTo />` in the library's csproj; `DynamicProxyGenAssembly2` gets the same from `test/Directory.Build.props`, which is what lets NSubstitute mock internal types.
- **The `Async` suffix is used.** Every operation here has a genuinely synchronous counterpart, so the suffix is what tells the pair apart - `Scan` and `ScanAsync`. Without a pair there would be nothing for it to distinguish and it would be noise.
- **The blocking half takes no `CancellationToken`** - not on the client, not on `ISpamAssassinConnectionSource.Connect`. A token cannot interrupt a blocking read that is already in flight, so one there would be checked between operations and promise more than it delivers. A synchronous caller bounds the work with a timeout on the streams it supplies.
- **Nothing public returns `ValueTask`.** Every public operation talks to a socket and therefore always suspends, which is precisely the case where `ValueTask` saves nothing and still imposes await-once on the caller. It survives only on `DisposeAsync`, where the framework dictates it, and on four internal reader methods plus `CompressAsync`, which do complete synchronously and have one call site each. See CONTRIBUTING.md for the rule.
- `ConfigureAwait(false)` on every await, enforced by `ConfigureAwaitChecker.Analyzer`, which the library references.
- The library has **exactly one runtime package reference**, `Microsoft.IO.RecyclableMemoryStream`, and a second one would be a decision rather than a convenience. Compression needs nothing: zlib is `System.IO.Compression.ZLibStream`, in the box since net6.
- **`SymbolsResult` hand-writes `Equals` and `GetHashCode`**, and has to: it is the one result type holding a collection, and a record compares its members - a member holding a list by identity - so two results from two identical scans came out unequal. Equality is now the verdict plus the rule names in order. Any future result type that gains a collection needs the same treatment.
- **No static classes, and no `static` shared state.** A helper that would have been a static class takes its invariants in a constructor instead, which is also what makes the wire layer testable in pieces.
- Member order follows StyleCop's: fields, constructors, properties, methods, and within each kind public before private. It is not analyzer-enforced here - StyleCop.Analyzers is not referenced - so it is a review matter.
- `.githooks/pre-commit` stamps the licence header into every staged `.cs` file, taking the name from `git config user.name`. It is not installed by default: `git config core.hooksPath .githooks`, once per clone.
