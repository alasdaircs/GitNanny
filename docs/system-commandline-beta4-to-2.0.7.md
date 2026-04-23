# System.CommandLine: beta4 → 2.0.7 Migration Guide

This guide covers every breaking API change between `System.CommandLine 2.0.0-beta4`
and `2.0.7` (the first stable release). All patterns were verified by decompiling the
2.0.7 assembly.

---

## Package reference

```xml
<!-- beta4 -->
<PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />

<!-- 2.0.7 -->
<PackageReference Include="System.CommandLine" Version="2.0.7" />
```

The 2.0.7 package targets `net8.0` / `netstandard2.0` (no `net10.0` TFM). .NET 10
projects pick up `net8.0` automatically via compatibility fallback.

---

## Breaking changes — full reference

### 1. Option constructor

The second `string` parameter changed meaning from **description** to **alias**.
Description is now a property.

```csharp
// beta4
var opt = new Option<string>("--output", "Where to write the file");

// 2.0.7
var opt = new Option<string>("--output")
{
    Description = "Where to write the file"
};
```

If you pass a description string to the constructor in 2.0.7 it is silently treated
as an alias, causing a runtime `ArgumentException` (aliases cannot contain
whitespace).

To add aliases alongside the name, use the `params string[]` second parameter:

```csharp
var opt = new Option<string>("--output", "-o")
{
    Description = "Where to write the file"
};
```

---

### 2. Handler registration — `SetHandler` → `SetAction`

`SetHandler` (an extension method on `Command`) no longer exists. The replacement is
`command.SetAction(...)`, an instance method on `Command`.

#### 2a. Async handler (most common case)

```csharp
// beta4
rootCommand.SetHandler(async (InvocationContext context) =>
{
    var value = context.ParseResult.GetValueForOption(myOption);
    context.ExitCode = await DoWorkAsync(value);
});

// 2.0.7
rootCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
{
    var value = parseResult.GetValue(myOption);
    return await DoWorkAsync(value);   // return int exit code directly
});
```

Key differences:
- Parameter type changes from `InvocationContext` to `(ParseResult, CancellationToken)`.
- Exit code is the **return value** of the delegate (`Task<int>`), not a property on the context.
- `CancellationToken` is wired to process termination automatically.

#### 2b. Sync handler

```csharp
// beta4
rootCommand.SetHandler((InvocationContext context) =>
{
    context.ExitCode = DoWork();
});

// 2.0.7
rootCommand.SetAction((ParseResult parseResult) =>
{
    return DoWork();   // return int
});
```

#### `SetAction` overload summary

| Signature | Notes |
|---|---|
| `SetAction(Action<ParseResult>)` | Sync, exit code always 0 |
| `SetAction(Func<ParseResult, int>)` | Sync, returns exit code |
| `SetAction(Func<ParseResult, CancellationToken, Task>)` | Async, exit code always 0 |
| `SetAction(Func<ParseResult, CancellationToken, Task<int>>)` | **Preferred async** — returns exit code |
| `SetAction(Func<ParseResult, Task>)` | `[EditorBrowsable(Never)]` — avoid |
| `SetAction(Func<ParseResult, Task<int>>)` | `[EditorBrowsable(Never)]` — avoid |

---

### 3. Reading option values

```csharp
// beta4
var value = context.ParseResult.GetValueForOption(myOption);

// 2.0.7
var value = parseResult.GetValue(myOption);
```

`GetValue<T>` is a generic method on `ParseResult`. The option type is inferred from
the `Option<T>` variable, so no explicit type argument is usually needed.

---

### 4. Invoking the command

```csharp
// beta4
return await rootCommand.InvokeAsync(args);   // Task<int>

// 2.0.7
return await rootCommand.Parse(args).InvokeAsync();   // also Task<int>
```

`Command.InvokeAsync(args)` no longer exists as a direct method. The new idiom is
explicit: parse first, then invoke.

`ParseResult.InvokeAsync` signature:

```csharp
public Task<int> InvokeAsync(
    InvocationConfiguration? configuration = null,
    CancellationToken cancellationToken = default)
```

Both parameters are optional. Pass `null` / `default` (or omit them) for standard
behaviour.

> **ILSpy note**: `get_type_members` reports the return type as `Task` (elides the
> generic argument). The decompiled source confirms it is `Task<int>`.

---

### 5. Removed types and namespaces

| beta4 type | 2.0.7 replacement |
|---|---|
| `System.CommandLine.Invocation.InvocationContext` | Removed from public API |
| `InvocationContext.ExitCode` | Return value of the `SetAction` delegate |
| `InvocationContext.ParseResult` | `ParseResult` passed directly to the delegate |
| `System.CommandLine.Handler` (static class) | Removed |
| `CommandExtensions.InvokeAsync(command, args, console)` | `command.Parse(args).InvokeAsync()` |

The `System.CommandLine.Builder` namespace and `CommandLineBuilder` / middleware
pipeline (`UseExceptionHandler`, `UseHelp`, etc.) are also removed. Built-in
exception handling and help are configured via `InvocationConfiguration` and option
properties.

---

## Complete before/after for a typical top-level program

### beta4

```csharp
using System.CommandLine;
using System.CommandLine.Invocation;

var outputOption = new Option<string>("--output", "Output path");
var verboseOption = new Option<bool>("--verbose", "Enable verbose logging");

var rootCommand = new RootCommand("My tool") { outputOption, verboseOption };

rootCommand.SetHandler(async (InvocationContext context) =>
{
    var output  = context.ParseResult.GetValueForOption(outputOption);
    var verbose = context.ParseResult.GetValueForOption(verboseOption);
    context.ExitCode = await RunAsync(output, verbose);
});

return await rootCommand.InvokeAsync(args);
```

### 2.0.7

```csharp
using System.CommandLine;

var outputOption = new Option<string>("--output")
{
    Description = "Output path"
};
var verboseOption = new Option<bool>("--verbose")
{
    Description = "Enable verbose logging"
};

var rootCommand = new RootCommand("My tool") { outputOption, verboseOption };

rootCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
{
    var output  = parseResult.GetValue(outputOption);
    var verbose = parseResult.GetValue(verboseOption);
    return await RunAsync(output, verbose);
});

return await rootCommand.Parse(args).InvokeAsync();
```

---

## Using ILSpy to verify the installed API

When upgrading, use the `mcp__ilspy` tools to inspect the actual installed DLL rather
than relying on documentation (which may lag behind).

Find the DLL:
```
C:\Dev\.nuget\packages\system.commandline\2.0.7\lib\net8.0\System.CommandLine.dll
```

Or in the build output after the first restore:
```
<project>\bin\Debug\net10.0\System.CommandLine.dll
```

> **Important**: Always inspect the NuGet cache DLL for the target version — the
> `bin\Debug` copy may be stale from a previous build against an older version.

Useful queries:
- `search_members_by_name` with `"SetAction"` or `"InvokeAsync"` to find the new API surface.
- `decompile_type` on `System.CommandLine.Command` to read full constructor and method signatures including generic parameters (member listings elide them).
- `decompile_type` on `System.CommandLine.ParseResult` to confirm `InvokeAsync` return type.
