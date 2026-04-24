using GitNanny;
using GitNanny.Ai;
using GitNanny.Configuration;
using GitNanny.Email;
using GitNanny.Git;
using GitNanny.Scanning;
using System.CommandLine;

var scanRootOption = new Option<string[]?>("--scan-root")
{
    Description = "Override scan roots from config (replaces, not appends)",
    Arity = ArgumentArity.ZeroOrMore,
    AllowMultipleArgumentsPerToken = true
};

var excludeOption = new Option<string[]?>("--exclude")
{
    Description = "Override exclude patterns for this run (replaces, not appends)",
    Arity = ArgumentArity.ZeroOrMore,
    AllowMultipleArgumentsPerToken = true
};

var maxDepthOption = new Option<int?>("--max-depth")
{
    Description = "Override recursion depth"
};

var dryRunOption = new Option<bool>("--dry-run")
{
    Description = "Scan and summarise; print HTML to stdout, do not send email"
};

var noAiOption = new Option<bool>("--no-ai")
{
    Description = "Skip Claude API calls; report raw data only"
};

var verboseOption = new Option<bool>("--verbose")
{
    Description = "Log each directory entered and each repo found"
};

var recipientOption = new Option<string[]?>("--recipient")
{
    Description = "Override recipient addresses (repeatable; replaces config value)",
    Arity = ArgumentArity.ZeroOrMore,
    AllowMultipleArgumentsPerToken = true
};

var rootCommand = new RootCommand("Git repository status report — scans repos and emails a summary")
{
    scanRootOption,
    excludeOption,
    maxDepthOption,
    dryRunOption,
    noAiOption,
    verboseOption,
    recipientOption
};

rootCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
{
    var cliScanRoots  = parseResult.GetValue(scanRootOption);
    var cliExcludes   = parseResult.GetValue(excludeOption);
    var cliMaxDepth   = parseResult.GetValue(maxDepthOption);
    var dryRun        = parseResult.GetValue(dryRunOption);
    var noAi          = parseResult.GetValue(noAiOption);
    var verbose       = parseResult.GetValue(verboseOption);
    var cliRecipients = parseResult.GetValue(recipientOption);

    var options = OptionsBuilder.BuildFromConfig();

    if (cliScanRoots?.Length > 0)
        options = options with { ScanRoots = cliScanRoots };
    if (cliExcludes?.Length > 0)
        options = options with { ExcludePatterns = cliExcludes };
    if (cliMaxDepth.HasValue)
        options = options with { MaxDepth = cliMaxDepth.Value };
    if (dryRun)
        options = options with { DryRun = true };
    if (noAi)
        options = options with { NoAi = true };
    if (verbose)
        options = options with { Verbose = true };
    if (cliRecipients?.Length > 0)
        options = options with { RecipientAddresses = cliRecipients };

    return await RunAsync(options);
});

return await rootCommand.Parse(args).InvokeAsync();

static async Task<int> RunAsync(AppOptions options)
{
    if (options.ScanRoots.Length == 0)
    {
        Console.Error.WriteLine("Error: ScanRoots must be configured. " +
            "Set ScanRoots in appsettings.json or pass --scan-root.");
        return 1;
    }

    if (!options.DryRun)
    {
        if (string.IsNullOrWhiteSpace(options.AzureClientId))
        {
            Console.Error.WriteLine("Error: AzureClientId must be configured in appsettings.json.");
            return 1;
        }

        if (options.RecipientAddresses.Length == 0)
        {
            Console.Error.WriteLine("Error: RecipientAddresses must be configured in appsettings.json or via --recipient.");
            return 1;
        }
    }

    if (options.Verbose)
        Console.Out.WriteLine("Scanning for repositories...");

    var repoPaths = RepoScanner.Discover(options);

    if (options.Verbose)
        Console.Out.WriteLine($"Found {repoPaths.Count} repo(s).");

    var statuses = new List<RepoStatus>();
    foreach (var path in repoPaths)
    {
        var status = GitInspector.Inspect(path);
        if (status is null)
            continue;

        if (options.Verbose)
            Console.Out.WriteLine(
                $"  {status.RepoName} [{status.BranchName}]: " +
                $"{status.UncommittedCount} uncommitted, " +
                $"{status.UnpushedCount} unpushed");

        statuses.Add(status);
    }

    var reposToReport = options.SkipCleanRepos
        ? statuses.Where(r => r.HasDirtyState).ToList()
        : statuses;

    if (reposToReport.Count == 0 && options.SkipCleanRepos)
    {
        if (options.Verbose)
            Console.Out.WriteLine("All repos are clean. Nothing to report.");
        return 0;
    }

    IReadOnlyList<RepoStatus> finalStatuses = reposToReport;
    if (!options.NoAi)
    {
        var apiKey = Environment.GetEnvironmentVariable("CLAUDE_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine(
                "Warning: CLAUDE_API_KEY environment variable not set. Skipping AI summaries.");
        }
        else
        {
            using var httpClient = new HttpClient();
            finalStatuses = await ClaudeSummariser.SummariseAsync(reposToReport, httpClient, apiKey);
        }
    }

    var message = ReportBuilder.Build(finalStatuses, options);

    if (options.DryRun)
    {
        Console.Out.WriteLine(message.HtmlBody);
        return 0;
    }

    try
    {
        await GraphSender.SendAsync(message, options.AzureClientId);
        if (options.Verbose)
            Console.Out.WriteLine("Report sent successfully.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: Failed to send email: {ex.Message}");
        return 1;
    }

    return 0;
}
