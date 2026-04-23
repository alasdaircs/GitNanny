# GitNanny

[![CI](https://github.com/alasdaircs/GitNanny/actions/workflows/ci.yml/badge.svg)](https://github.com/alasdaircs/GitNanny/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A .NET 10 Windows console application that scans local folders for Git repositories, summarises in-progress work using the Claude AI API, and sends an HTML email report via Microsoft 365.

Intended to run nightly via Windows Task Scheduler so you always know which repos have uncommitted or unpushed work.

---

## Features

- Recursively discovers Git repositories under configured root folders
- Respects `.gitignore` at every level; skips `bin`, `obj`, `node_modules`, etc.
- Reports uncommitted files (colour-coded by status), unpushed commits, and unpulled commits
- Calls the Claude API to generate a plain-English summary of what each repo appears to be working on
- Sends a styled HTML email via Microsoft Graph / Outlook (no SMTP required)
- Silent re-authentication after first sign-in (DPAPI-encrypted MSAL token cache)
- `--dry-run` mode prints the HTML report to stdout without sending anything

---

## Prerequisites

| Requirement | Notes |
|---|---|
| Windows 10/11 | DPAPI token cache is Windows-only |
| .NET 10 SDK | Build only; the published exe is self-contained |
| Microsoft Entra app registration | Delegated `Mail.Send` permission, public client, redirect URI `http://localhost` |
| Claude API key | Set as environment variable `CLAUDE_API_KEY` |

### Entra app registration

1. In the [Azure portal](https://portal.azure.com), create a new app registration (single tenant or multitenant as preferred).
2. Under **Authentication**, add `http://localhost` as a Mobile/desktop redirect URI and enable **Allow public client flows**.
3. Under **API permissions**, add the Microsoft Graph delegated permission `Mail.Send` and grant admin consent.
4. Copy the **Application (client) ID** — you will need it in `appsettings.json`.

---

## Installation

```powershell
git clone https://github.com/alasdaircs/GitNanny.git
cd GitNanny

# Publish a self-contained Windows exe
dotnet publish GitReport/GitReport.csproj -r win-x64 --self-contained -c Release -o publish/
```

The output in `publish/` is a single `GitReport.exe` with no runtime dependency.

---

## Configuration

Edit `GitReport/appsettings.json` before publishing (or alongside the exe):

```json
{
  "ScanRoots": ["C:\\Dev", "C:\\Users\\you\\source\\repos"],
  "ExcludePatterns": ["bin", "obj", "node_modules", ".git"],
  "MaxDepth": 5,
  "SkipCleanRepos": true,
  "AzureClientId": "<your-entra-client-id>",
  "RecipientAddress": "you@example.com"
}
```

Set the Claude API key as an environment variable (do not commit it):

```powershell
[System.Environment]::SetEnvironmentVariable("CLAUDE_API_KEY", "sk-ant-...", "User")
```

---

## Usage

```
GitReport.exe [options]

Options:
  --scan-root <path>    Override scan roots (repeatable; replaces config value)
  --exclude <pattern>   Add extra exclude patterns for this run
  --max-depth <n>       Override recursion depth
  --dry-run             Print HTML report to stdout; do not send email
  --no-ai               Skip Claude API calls; report raw data only
  --verbose             Log each directory entered and each repo found
```

### First run

The first execution opens a browser for Microsoft 365 sign-in. After signing in, the authentication record and MSAL token cache are stored under `%APPDATA%\GitReport\`. Subsequent runs are fully silent.

### Dry run (no email, no AI)

```powershell
GitReport.exe --dry-run --no-ai --scan-root C:\Dev
```

### Scheduled task

```powershell
schtasks /create /tn "GitNanny" /tr "C:\Tools\GitReport.exe" /sc DAILY /st 07:00 /ru "%USERNAME%"
```

---

## Project structure

```
GitReport/
  Ai/               Claude API summarisation
  Configuration/    appsettings + env + CLI wiring
  Email/            HTML report builder and Microsoft Graph sender
  Git/              LibGit2Sharp repository inspection
  Scanning/         Recursive repo discovery
  Program.cs        Entry point
  appsettings.json  Default configuration (no secrets)
```

---

## Licence

[MIT](LICENSE)
