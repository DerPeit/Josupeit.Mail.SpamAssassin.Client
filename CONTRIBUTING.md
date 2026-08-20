# Contributing

Conventions for this repository: **how code is written** and **how commits are worded**.
For what the project _is_ and how it is built, see [CLAUDE.md](CLAUDE.md).

Some of this is machine-enforced, most of it is not.

`.editorconfig` carries the naming rules, file layout and encoding, and `Directory.Build.props` sets `EnforceCodeStyleInBuild`, so a violation is a build warning rather than a mark in the editor - without that property the rules colour the IDE and nothing else. `ConfigureAwaitChecker.Analyzer` covers the `ConfigureAwait` rule. `src/Directory.Build.props` sets `GenerateDocumentationFile`, which is what turns the doc-comment rules below into build warnings: CS1591 for an undocumented public member, CS1573 for a parameter without its own tag. Neither is suppressed, and both are expected to stay at zero.

Everything else below is a convention no analyzer can express: statement grouping, the visibility tiers, the `ValueTask` policy, the sync/async pairing, the test layout. One naming rule is only _partly_ enforced - see the note on the exposed-constants rule in `.editorconfig`. Where a rule appears in both files, keep them saying the same thing.

## Code style

### Files & layout

- **One type per file**, file name == type name.
- **Folders mirror namespaces.**
- **File-scoped namespaces** (`namespace X;`).
- `using` directives **outside** the namespace, `System.*` first, then the rest, each block alphabetical. **No** global or implicit usings in the library.
- **4 spaces**, no tabs. **Allman** braces.
- Line endings: **CRLF** in the working tree (Git normalizes to LF in the repository).
- **Every `.cs` file opens with the licence header**, before the `using` block and separated from it by a blank line:

  ```csharp
  // SPDX-License-Identifier: EUPL-1.2
  // Copyright (c) Manuel Josupeit-Walter
  ```

  A contributor who touches a file adds their own `// Copyright (c) <name>` line under the existing ones. This is not busywork to do by hand - `.githooks/pre-commit` stamps it into every staged file, taking the name from `git config user.name`. The hook is not installed by default, so enable it once per clone:

  ```bash
  git config core.hooksPath .githooks
  ```

### Naming & visibility

- **Two visibility tiers**, not one default:
  - **The authoring surface** - `ISpamAssassinClient`, `SpamAssassinClient`, the connection abstractions, the options record, the result types, the enums a caller passes in and the exceptions. Public, and everything a consumer touches.
  - **The wire layer** - everything under `Wire/`, `internal sealed`. None of it is a public extension point: the transport is already the caller's, so there is nothing left worth subclassing. Reachable from the two test projects through `<InternalsVisibleTo />` and from nowhere else.
- **Inside an `internal` type, members are `public`.** The type is the boundary; an `internal` member of an `internal` class restricts nothing the class did not already restrict, and reading it as "more hidden than the rest" is a mistake waiting to happen. `private` still means private.
- **No static classes.** A helper that wants to be one takes its invariants through a constructor instead - which is also what keeps the wire layer testable in pieces, and what keeps shared state from appearing by accident.
- **Member order is StyleCop's**: fields, constructors, properties, methods, nested types; and within each kind, public before internal before protected before private, static before instance. Nothing enforces it - StyleCop.Analyzers is not referenced - so it is a review matter.
- **Sealed unless it is meant to be derived from.** A public type that is not sealed is a statement that overriding it is supported; do not unseal one just to make a test easier.
- **Validate arguments on the public surface**, not on internal members. An internal method is only reached by this library and its tests, so a guard there checks a caller that cannot get it wrong.
- **`const` is `SCREAMING_SNAKE_CASE`** - field or local. The case marks the value as baked in at compile time.
- **The screaming snake stops at the API boundary.** It is not idiomatic C#, so nothing visible from outside wears it: the public surface is `PascalCase` throughout, constants included.
- **Private fields are `_camelCase`**, including `static readonly` ones.
- Locals and parameters `camelCase`; everything else `PascalCase`.
- Interfaces are `I`-prefixed.
- **The `Async` suffix is used**, because every operation here has a genuinely synchronous counterpart and the suffix is what tells the pair apart: `Scan` blocks, `ScanAsync` does not. Where nothing has a sync twin the suffix distinguishes nothing and is noise; that is not the case here.
- Names spell out the domain term rather than the wire mnemonic. Map the spamc/spamd name in a comment or an enum member instead.

### Comments

Two kinds, and which one to use is decided by who can see the member - not by how much there is to say.

- **`///` doc comments are for the public surface, and nothing else**: public members of public types, protected members of public types. They are what a consumer reads in IntelliSense and what ends up in the generated XML file. A non-public member never gets one.
- **`//` comments everywhere else** - non-public members of the library, and every line of the test projects. Same prose, no tags: `<see cref="X"/>` becomes the bare name, `<see langword="null"/>` becomes `null`, a `<summary>` and a `<remarks>` become one block split by a bare `//` line.
- **A doc comment is complete or it is wrong.** `<summary>` always, plus a tag for every parameter, type parameter, return value and exception the member documents. Half a doc comment is worse than none: IntelliSense shows the summary and silently omits the parameter the caller was actually asking about.
- **`<inheritdoc />` wherever a base or interface already says it.** An implementation of `ISpamAssassinClient` repeats nothing; the one description then cannot drift from its copies.
- Explain **why**, not what. The signature already says what a member is called and what it takes. The doc comment earns its place by saying what the caller cannot see: what is guaranteed, what is not, which stream is read and which is written, what happens to the connection when the call returns, and which mistake the API is shaped to prevent.

### Language features

`LangVersion` is `latest`, `Nullable` is `enable`. Use modern C# freely:

- **`var`** for locals; **target-typed `new()`** for returns and field initializers.
- **Primary constructors** for injected dependencies - **only where readability does not suffer**:
  - The parameter list must **fit on the declaration line**. If it would wrap, write an explicit constructor instead.
  - As soon as a **second constructor** is needed, **all** constructors are explicit. Never mix a primary constructor with an overload.
- **Records** for data: `sealed record` for options and results, **`readonly record struct`** for small payloads that are only passed along.
- **Switch expressions** with a trailing comma on the last arm; blank line between the arms of a switch _statement_.
- Nullable annotations and `??=`, `?.` where they shorten the code.
- **Omit braces** for single-statement `if` / `foreach` / `await foreach` bodies. Braces once the body spans more than one line.
- Package references use **bounded version ranges** (`[8,9)`), not floating versions.

### Statement grouping

**Insert a blank line at every transition between statement groups; no blank line within a group.**

| Group                 | Examples                                     |
| --------------------- | -------------------------------------------- |
| Variable declarations | `var x = ...;` `Type x = ...;`               |
| Assignments           | `x = ...;` `x.Prop = ...;`                   |
| Invocations           | `x.Method(...);` (local-variable receiver)   |
| Static invocations    | `Type.Method(...);`                          |
| Keywords              | `return` / `await` / `throw` / `yield`       |
| Multi-line statements | always alone - blank line before _and_ after |

Classify by the **outermost** construct:

- `await x.Foo()` -> keyword (not an invocation)
- `var x = await x.Foo()` -> variable declaration
- `var x = new Foo { ... }` spanning lines -> multi-line statement

An extra blank line between two groups of the _same_ kind is fine when it separates logical steps.

### Async, and the synchronous twin

- **`ConfigureAwait(false)` on every await** - enforced by `ConfigureAwaitChecker.Analyzer`; do not omit it. In a library it is still a must, and this is a library: it cannot know what its caller awaits on. In application code it is typically redundant nowadays and is correspondingly rare in the wild, which is why the sample under `samples/` has none - a sample is user code, and it should look like the code a consumer actually writes. Do not add them back there.
- **Every public operation exists twice**, and the synchronous half is **genuinely synchronous**: it calls `Stream.Read`/`Stream.Write`. `GetAwaiter().GetResult()` and `Task.Run(...).Result` are not implementations, they are deadlocks waiting for a host with a synchronization context.
- **Write the protocol once.** Two copies of the request writer, one per colour, will drift within a month. Whatever seam is chosen to share the logic, it is the shared thing that gets the test coverage, and both entry points are asserted to produce byte-identical output.
- **`Task` is the default; `ValueTask` has to earn its place.** This is Microsoft's own guidance and it is the rule here: the default choice for an asynchronous method is `Task`/`Task<T>`, and `ValueTask` is for cases where an analysis says it pays. "`ValueTask<T>` is never worse" is true only of the allocation, and the allocation is not the only cost:
  - it may be **awaited exactly once**, may not be blocked on, and may not be handed to `WhenAll`/`WhenAny` without `.AsTask()` - so it moves a rule the compiler does not check onto every caller;
  - the saving exists **only when the operation completes synchronously**. On suspension the two are equal, so a method that always performs I/O saves nothing and complicates its callers for free.
- **What that means concretely.** Nothing on the public surface returns `ValueTask`, because every public operation here talks to a socket and therefore always suspends - a `ValueTask` would be a constraint on consumers bought with no saving. `ValueTask` survives in exactly two places:
  - **`IAsyncDisposable.DisposeAsync`**, where the framework dictates the type;
  - **internal methods that genuinely do complete synchronously and have exactly one call site each** - the response reader's `ReadHeadAsync`, `CopyBodyAsync`, `ReadTextAsync` and `FillAsync`, whose `Stream.ReadAsync` returns synchronously whenever the bytes are already buffered, and `RequestWriter.CompressAsync`, which never suspends for an in-memory message. Internal, single-call-site and awaited once is what makes the constraint free to accept.
- A non-generic `ValueTask` almost never makes sense: an `async Task` that completes synchronously already hands back the cached `Task.CompletedTask`, so it is **0 against 0**.
- `CancellationToken` is the **last parameter** and is always passed on. Async iterators take `[EnumeratorCancellation] CancellationToken`.

### Performance mindset

Not rigid rules - lenses. When writing, reading or reviewing code here, actively look for these and **surface the opportunity even when not taking it**:

- **struct vs class**: is this copied on a hot path? Would `in` / `ref readonly` remove copies without mangling the call chain? Does `Span<T>`, `stackalloc` or `CollectionsMarshal` fit?
- **Buffers**: a message is the biggest thing that moves through here. `ArrayPool<T>`, `ReadOnlySequence<byte>` and copying straight into the caller's destination stream are what keep it off the large object heap. Never materialize a body into a `byte[]` just to hand it somewhere.
- **Parsing**: the header block is small and read once. `SearchValues`, `Utf8Parser` and `ReadOnlySpan<char>.Split` beat `string.Split` plus `Trim` plus `double.Parse` on a substring, and they beat it without costing readability.
- **`ValueTask` vs `Task`**: see the async rules above.
- **Readability wins.** No manual state machines, no `unsafe` for marginal gains. If an optimization meaningfully hurts readability, say so and weigh the trade-off explicitly rather than silently skipping it.

### Tests

- **Arrange / Act / Assert**, always separated by blank lines - and by nothing else. `// Arrange` and its siblings only label what the blank lines already show, so do not write them. A comment block is always preceded by a blank line.
- **Invented test data only**, never anything real: `example.com` addresses, a fresh name per file, and funny where the context allows.
- Two projects, and the split decides where a test belongs. `Josupeit.Mail.SpamAssassin.Client.UnitTests` needs no Docker and covers the wire format byte-exactly plus everything a correct spamd never sends - a truncated response, a missing `Content-length`, a body shorter than announced, junk in the status line. `Josupeit.Mail.SpamAssassin.Client.IntegrationTests` drives a real `spamd` through Testcontainers; see [CLAUDE.md](CLAUDE.md).
- **Reach for the unit project first.** An integration run costs minutes and a Docker daemon; it earns that only when the point of the test is that spamd agrees.
- **CI runs only the unit project.** Codeberg's runners hand out no Docker daemon, so an integration test is a gate you apply yourself: run `dotnet test Josupeit.Mail.SpamAssassin.Client.slnx` locally before pushing anything that touches the wire.
- **Both colours of every operation are tested.** A sync overload that nobody exercised is a sync overload that blocks forever on the first stream that suspends.
- **Bound every wait on a background worker.** A test that awaits a task which never completes hangs the whole run rather than failing one test, and NUnit's `CancelAfter` cannot break it. Race the wait against a timeout and assert on which won.

## Commit style

Commits follow [Conventional Commits 1.0.0](https://www.conventionalcommits.org/en/v1.0.0/#specification): `<type>[optional scope]: <description>`, optionally followed by a body and footers.

- Common types here: `feat`, `fix`, `refactor`, `perf`, `test`, `docs`, `build`, `chore`.
- The description is **third-person present** ("adds", "fixes", "moves"), lowercase, no trailing period.
- Write a body in most cases: the **reasoning** behind the change, not a restatement of the diff. Keep it brief - one or two sentences.
- Breaking changes: `!` after the type/scope and/or a `BREAKING CHANGE:` footer.
- No `Co-Authored-By` trailers.

```
feat(client): adds the CHECK command and the Spam header parser

The verdict is the one command every caller needs, so it goes first and
sets the shape the remaining eight are written against.
```
