namespace GitReport;

record AppOptions
{
    public string[]  ScanRoots        { get; init; } = [];
    public string[]  ExcludePatterns  { get; init; } = ["bin", "obj", "node_modules", ".git"];
    public int       MaxDepth         { get; init; } = 5;
    public bool      SkipCleanRepos   { get; init; } = true;
    public string    AzureClientId    { get; init; } = "";
    public string    RecipientAddress { get; init; } = "";
    public bool      DryRun           { get; init; } = false;
    public bool      NoAi             { get; init; } = false;
    public bool      Verbose          { get; init; } = false;
}
