using GitReport;
using GitReport.Ai;
using GitReport.Configuration;
using GitReport.Email;
using GitReport.Git;
using GitReport.Scanning;
using System.CommandLine;
using System.CommandLine.Invocation;

var scanRootOption = new Option<string[]?>(
    "--scan-root",
    "Override scan roots from config (replaces, not appends)")
{
    Arity = ArgumentArity.ZeroOrMore,
    AllowMultipleArgumentsPerToken = true
};

var excludeOption = new Option<string[]?>(
    "--exclude",
    "Override exclude patterns for this run (replaces, not appends)")
{
    Arity = ArgumentArity.ZeroOrMore,
    AllowMultipleArgumentsPerToken = true
};

var maxDepthOption = new Option<int?>(
    "--max-depth",
    "Override recursion depth");

var dryRunOption = new Option<bool>(
    "--dry-run",
    "Scan and summarise; print HTML to stdout, do not send email");

var noAiOption = new Option<bool>(
    "--no-ai",
    "Skip Claude API calls; report raw data only");

var verboseOption = new Option<bool>(
    "--verbose",
    "Log each directory entered and each repo found");

var rootCommand = new RootCommand("Git repository status report — scans repos and emails a summary")
{
    scanRootOption,
    excludeOption,
    maxDepthOption,
    dryRunOption,
    noAiOption,
    verboseOption
};

rootCommand.SetHandler(async (InvocationContext context) =>
{
    var cliScanRoots = context.ParseResult.GetValueForOption(scanRootOption);
    var cliExcludes  = context.ParseResult.GetValueForOption(excludeOption);
    var cliMaxDepth  = context.ParseResult.GetValueForOption(maxDepthOption);
    var dryRun       = context.ParseResult.GetValueForOption(dryRunOption);
    var noAi         = context.ParseResult.GetValueForOption(noAiOption);
    var verbose      = context.ParseResult.GetValueForOption(verboseOption);

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

    context.ExitCode = await RunAsync(options);
});

return await rootCommand.InvokeAsync(args);

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

        if (string.IsNullOrWhiteSpace(options.RecipientAddress))
        {
            Console.Error.WriteLine("Error: RecipientAddress must be configured in appsettings.json.");
            return 1;
        }
    }

    // Phase 1: discover repos
    if (options.Verbose)
        Console.Out.WriteLine("Scanning for repositories...");

    var repoPaths = RepoScanner.Discover(options);

    if (options.Verbose)
        Console.Out.WriteLine($"Found {repoPaths.Count} repo(s).");

    // Phase 2: collect Git data
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
        ? statuses.Where(r => r.UncommittedCount > 0 || r.UnpushedCount > 0).ToList()
        : statuses;

    if (reposToReport.Count == 0 && options.SkipCleanRepos)
    {
        if (options.Verbose)
            Console.Out.WriteLine("All repos are clean. Nothing to report.");
        return 0;
    }

    // Phase 3: AI summarisation
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

    // Phase 4: build and send (or print) report
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
