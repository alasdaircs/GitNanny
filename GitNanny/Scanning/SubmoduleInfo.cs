namespace GitNanny.Scanning;

record SubmoduleInfo
{
    public string      Name          { get; init; } = "";
    public string      RelativePath  { get; init; } = "";
    public bool        IsInitialized { get; init; }
    public RepoStatus? Status        { get; init; }  // null if not initialised or failed to open
}
