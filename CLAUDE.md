# GitNanny — implementation brief for Claude Code

## What this is

A .NET 10 C# console application that runs nightly via Windows Task Scheduler. It
recursively scans configured folders for Git repositories, collects status data for
each dirty repo, calls the Claude API to generate a plain-English summary of
in-progress work, and sends an HTML email report via Microsoft Graph.

---

## Non-negotiable decisions — do not second-guess these

- **LibGit2Sharp** for all Git data. No shelling out to `git.exe`, no `Process.Start`.
- **Microsoft Graph `users/{id}/sendMail`** for sending email. No SMTP, no MailKit
  transport. MailKit is used only to construct the MIME message body.
- **`System.CommandLine`** (the official Microsoft preview package) for CLI option
  parsing. Not `CommandLineParser`, not manual `args[]` parsing.
- **`Microsoft.Extensions.FileSystemGlobbing`** for exclude pattern matching. No
  custom glob code, no regex.
- **`Microsoft.Extensions.Configuration`** for settings, with standard .NET layering:
  `appsettings.json` → environment variables → CLI flags (highest wins).
- **Self-contained Windows x64 publish** (`dotnet publish -r win-x64
  --self-contained`). No runtime dependency on the target machine.
- **No secrets in `appsettings.json`**. The Claude API key comes from an environment
  variable. Graph auth uses an MSAL token cache (see Auth section).

---

## Project structure

Create a single solution with one console project. Do not create separate class
library projects — this tool is small enough that logical folders within one project
are sufficient.

```
GitNanny/
  GitNanny.sln
  GitNanny/
    GitNanny.csproj
    Program.cs
    AppOptions.cs
    Scanning/
      RepoScanner.cs       # walks filesystem, discovers repos
      RepoStatus.cs        # data record per repo
    Git/
      GitInspector.cs      # LibGit2Sharp queries
    Ai/
      ClaudeSummariser.cs  # Claude API calls
    Email/
      ReportBuilder.cs     # builds MimeKit MimeMessage
      GraphSender.cs       # sends via Microsoft Graph
    Configuration/
      OptionsBuilder.cs    # wires appsettings + env + CLI into AppOptions
    appsettings.json
```

---

## NuGet packages

```xml
<PackageReference Include="LibGit2Sharp" Version="*" />
<PackageReference Include="MimeKit" Version="*" />
<PackageReference Include="Microsoft.Graph" Version="*" />
<PackageReference Include="Azure.Identity" Version="*" />
<PackageReference Include="System.CommandLine" Version="2.0.0-beta*" />
<PackageReference Include="Microsoft.Extensions.Configuration" Version="*" />
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="*" />
<PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="*" />
<PackageReference Include="Microsoft.Extensions.FileSystemGlobbing" Version="*" />
</PackageReference Include="System.Text.Json" Version="*" />
```

Use the latest stable version for each unless noted.

---

## AppOptions record

All configuration flows into a single `AppOptions` record. CLI flags bind into this
same record, overriding lower-precedence sources.

```csharp
record AppOptions
{
    public string[]  ScanRoots        { get; init; } = [];
    public string[]  ExcludePatterns  { get; init; } = ["bin", "obj", "node_modules", ".git"];
    public int       MaxDepth         { get; init; } = 5;
    public bool      SkipCleanRepos   { get; init; } = true;
    public string    AzureClientId    { get; init; } = "";
    public string    RecipientAddress { get; init; } = "";
    // Runtime-only flags (CLI only, not in appsettings)
    public bool      DryRun           { get; init; } = false;
    public bool      NoAi             { get; init; } = false;
    public bool      Verbose          { get; init; } = false;
}
```

---

## CLI options (System.CommandLine)

| Flag | Type | Description |
|---|---|---|
| `--scan-root` | `string[]` (repeatable) | Override scan roots from config |
| `--exclude` | `string[]` (repeatable) | Add extra exclude patterns for this run |
| `--max-depth` | `int` | Override recursion depth |
| `--dry-run` | `bool` flag | Scan and summarise; print to console, do not send email |
| `--no-ai` | `bool` flag | Skip Claude API calls; report raw data only |
| `--verbose` | `bool` flag | Log each repo as it is scanned |

When `--scan-root` or `--exclude` are provided on the CLI, they **replace** (not
append to) the values from config. This is simpler and more predictable for ad-hoc
use.

---

## Phase 1 — repo discovery

`RepoScanner` walks each root in `ScanRoots` recursively.

- Before descending into any subdirectory, test its **name** (not full path) against
  each pattern in `ExcludePatterns` using `Microsoft.Extensions.FileSystemGlobbing`
  `Matcher`. If any pattern matches, skip the directory entirely — do not descend.
- Respect `MaxDepth` as a hard ceiling.
- When a directory contains a `.git` subfolder, record it as a candidate repo and do
  **not** descend further into it (repos are not nested).
- Pass each candidate path to `GitInspector`. Wrap in try/catch — if
  `LibGit2Sharp.Repository` throws (bare repo, corrupt), log a warning and continue.
- If `--verbose`, log each directory entered and each repo found.

---

## Phase 2 — per-repo data collection (LibGit2Sharp)

`GitInspector` produces a `RepoStatus` record for each valid repository.

**Uncommitted changes**

Use `repo.RetrieveStatus(new StatusOptions { IncludeUntracked = true })`. Collect:
- Count of entries across all status states (staged, modified, deleted, untracked).
- The list of relative file paths (for the AI prompt).

**Oldest uncommitted change**

For each dirty file path, resolve the absolute path and call `File.GetLastWriteTimeUtc`.
Take the minimum. If no dirty files, this field is null.

**Unpushed commits**

- Find the tracking branch: `repo.Head.TrackedBranch`. If null, the repo has no
  remote tracking branch — mark as "local only" and skip push/pull counts.
- Walk `repo.Commits` starting from HEAD. Stop when you reach a commit that is an
  ancestor of (or equal to) the tracking branch tip. Collect `Commit.Message` (first
  line only) and `Commit.Author.When` for each.

**Unpulled count**

Compare `repo.Head.TrackingDetails.BehindBy`. This uses the last-fetched remote ref —
no network call. It may be stale; that is acceptable and should be noted in the email
as "as of last fetch".

**RepoStatus record**

```csharp
record RepoStatus
{
    public string        RepoPath            { get; init; }
    public string        RepoName            { get; init; }  // directory name
    public string        BranchName          { get; init; }
    public bool          IsLocalOnly         { get; init; }
    public int           UncommittedCount    { get; init; }
    public string[]      UncommittedFiles    { get; init; }
    public DateTime?     OldestChangeUtc     { get; init; }
    public int           UnpushedCount       { get; init; }
    public string[]      UnpushedMessages    { get; init; }
    public int?          UnpulledCount       { get; init; }  // null if local only
    public string?       AiSummary           { get; init; }  // filled later
}
```

---

## Phase 3 — AI summarisation

`ClaudeSummariser` calls the Claude API for each repo where
`UncommittedCount > 0 || UnpushedCount > 0`, unless `--no-ai` is set.

- Read API key from environment variable `CLAUDE_API_KEY`. If missing, log a warning
  and behave as if `--no-ai` were set.
- Use `HttpClient` + `System.Text.Json`. Do not add an Anthropic SDK package.
- Model: `claude-haiku-4-5` (fast and cheap for this task).
- Max tokens: 150.
- Make calls sequentially (not parallel) to stay within rate limits.

**Prompt template**

```
You are summarising the state of a Git repository for a developer's daily report.
Repository: {RepoName} (branch: {BranchName})
Uncommitted files ({UncommittedCount}): {comma-separated list, truncated to 20 files}
Unpushed commits ({UnpushedCount}): {newline-separated messages, truncated to 10}
Summarise what work is in progress or waiting to be pushed in 1–3 plain English
sentences. Be specific about what the work appears to involve.
```

Truncate file lists and commit messages in the prompt to keep token usage bounded.

---

## Phase 4 — email construction and sending

**Building the message (MimeKit)**

`ReportBuilder` constructs a `MimeMessage` with an HTML body.

Structure:
- Subject: `"Git report — {n} repos need attention"` or `"Git report — all repos
  clean"` if no dirty repos found (and `SkipCleanRepos` is false and all are clean).
- One `<section>` per repo with:
  - Repo name and branch as a heading.
  - Summary table: uncommitted count, oldest change date, unpushed count, unpulled
    count (labelled "as of last fetch").
  - "Local only" badge if no remote.
  - AI summary paragraph, or omitted if `--no-ai`.
- Keep the HTML simple — inline styles only, no external CSS, renders well in
  Outlook.

**Sending (Microsoft Graph)**

`GraphSender` sends the constructed message.

Auth:
- Use `Azure.Identity.InteractiveBrowserCredential` with a persistent MSAL token
  cache stored in the user profile directory (DPAPI-encrypted on Windows).
- Token cache path: `%APPDATA%\GitNanny\msal_cache.bin`.
- On first run the browser opens for sign-in. Subsequent runs are silent.
- Required scope: `Mail.Send` (delegated).
- `AzureClientId` from `AppOptions` is the Entra app registration client ID.

Sending:
- Convert the `MimeMessage` to a base64-encoded string.
- POST to `https://graph.microsoft.com/v1.0/me/sendMail` using the Graph SDK
  `SendMailPostRequestBody` with `saveToSentItems: false`.

If `--dry-run`, skip `GraphSender` entirely and write the HTML to stdout (or a temp
file opened in the default browser, whichever is easier).

---

## appsettings.json defaults

```json
{
  "ScanRoots": [],
  "ExcludePatterns": ["bin", "obj", "node_modules", ".git"],
  "MaxDepth": 5,
  "SkipCleanRepos": true,
  "AzureClientId": "",
  "RecipientAddress": ""
}
```

`ScanRoots`, `AzureClientId`, and `RecipientAddress` must be populated by the user
before first run. The app should exit with a clear error message if any of these are
empty.

---

## Error handling and logging

- Use `Console.Error` for warnings and errors. Use `Console.Out` for normal output
  (repo scan results in `--verbose`, dry-run HTML).
- The app should exit with code 0 on success (even if all repos are clean), and
  non-zero on any unrecoverable error (missing config, Graph auth failure).
- Individual repo errors (LibGit2Sharp throws, file not accessible) are warnings —
  log and continue, do not abort the run.
- Claude API errors for a specific repo: log warning, leave `AiSummary` null, include
  the repo in the report without a summary.

---

## What "done" looks like

- `dotnet build` produces no errors or warnings.
- `dotnet run -- --dry-run --no-ai --scan-root C:\Code` prints a readable summary of
  repos found and their status, then exits 0.
- `dotnet publish -r win-x64 --self-contained -c Release` produces a single exe.
- The scheduled task invocation (no flags) runs silently and sends the email, or
  exits 0 with no output if all repos are clean and `SkipCleanRepos` is true.

---

## Out of scope

- Fetching from remotes (unpulled count uses last-known state only).
- Multiple recipients.
- Repos on network drives or UNC paths (not tested, not required).
- Any GUI.
