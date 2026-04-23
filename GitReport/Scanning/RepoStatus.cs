namespace GitReport.Scanning;

record RepoStatus
{
    public string    RepoPath         { get; init; } = "";
    public string    RepoName         { get; init; } = "";
    public string    BranchName       { get; init; } = "";
    public bool      IsLocalOnly      { get; init; }
    public int       UncommittedCount { get; init; }
    public string[]  UncommittedFiles { get; init; } = [];
    public DateTime? OldestChangeUtc  { get; init; }
    public int       UnpushedCount    { get; init; }
    public string[]  UnpushedMessages { get; init; } = [];
    public int?      UnpulledCount    { get; init; }
    public string?   AiSummary        { get; init; }
}
