namespace GitNanny.Scanning;

record RepoStatus
{
    public string    RepoPath            { get; init; } = "";
    public string    RepoName            { get; init; } = "";
    public string    BranchName          { get; init; } = "";
    public string?   GitUserName         { get; init; }
    public string?   GitUserEmail        { get; init; }
    public bool      IsLocalOnly         { get; init; }
    public int       UncommittedCount    { get; init; }
    public string[]  UncommittedFiles    { get; init; } = [];  // raw paths, used for AI prompt
    public string[]  UncommittedEntries  { get; init; } = [];  // "[M] path/to/file", used for display
    public DateTime? OldestChangeUtc     { get; init; }
    public int       UnpushedCount       { get; init; }
    public string[]  UnpushedMessages    { get; init; } = [];  // "yyyy-MM-dd hash7 message"
    public int?      UnpulledCount       { get; init; }
    public string[]  UnpulledMessages    { get; init; } = [];  // "yyyy-MM-dd hash7 message"
    public string?                             UncommittedDiff   { get; init; }  // truncated unified diff for AI prompt
    public IReadOnlyDictionary<string, string> UntrackedSnippets { get; init; } = new Dictionary<string, string>();  // path → first ~500 chars
    public IReadOnlyList<SubmoduleInfo>        Submodules        { get; init; } = [];
    public string?                             AiSummary         { get; init; }

    public bool HasDirtyState =>
        UncommittedCount > 0 || UnpushedCount > 0 ||
        Submodules.Any(s => s.Status?.HasDirtyState == true);
}
