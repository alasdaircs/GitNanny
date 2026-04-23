namespace GitReport.Scanning;

record RepoStatus
{
    public string    RepoPath            { get; init; } = "";
    public string    RepoName            { get; init; } = "";
    public string    BranchName          { get; init; } = "";
    public bool      IsLocalOnly         { get; init; }
    public int       UncommittedCount    { get; init; }
    public string[]  UncommittedFiles    { get; init; } = [];  // raw paths, used for AI prompt
    public string[]  UncommittedEntries  { get; init; } = [];  // "[M] path/to/file", used for display
    public DateTime? OldestChangeUtc     { get; init; }
    public int       UnpushedCount       { get; init; }
    public string[]  UnpushedMessages    { get; init; } = [];  // "yyyy-MM-dd hash7 message"
    public int?      UnpulledCount       { get; init; }
    public string[]  UnpulledMessages    { get; init; } = [];  // "yyyy-MM-dd hash7 message"
    public string?   AiSummary           { get; init; }
}
