using GitNanny.Scanning;
using LibGit2Sharp;

namespace GitNanny.Git;

static class GitInspector
{
    public static RepoStatus? Inspect(string repoPath)
    {
        try
        {
            using var repo = new Repository(repoPath);

            var statusOptions = new StatusOptions
            {
                IncludeUntracked      = true,
                RecurseUntrackedDirs  = true
            };
            var status = repo.RetrieveStatus(statusOptions)
                             .Where(e => !e.State.HasFlag(FileStatus.Ignored))
                             .ToList();

            var uncommittedFiles   = status.Select(e => e.FilePath).ToArray();
            var uncommittedEntries = status.Select(e => $"[{StatusChar(e.State)}] {e.FilePath}").ToArray();
            var uncommittedCount   = uncommittedFiles.Length;

            DateTime? oldestChange = null;
            foreach (var filePath in uncommittedFiles)
            {
                var absPath = Path.Combine(
                    repoPath,
                    filePath.Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(absPath))
                {
                    var lastWrite = File.GetLastWriteTimeUtc(absPath);
                    if (oldestChange is null || lastWrite < oldestChange)
                        oldestChange = lastWrite;
                }
            }

            var isLocalOnly      = repo.Head.TrackedBranch is null;
            var unpushedMessages = Array.Empty<string>();
            var unpushedCount    = 0;
            var unpulledMessages = Array.Empty<string>();
            int? unpulledCount   = null;

            if (!isLocalOnly && repo.Head.TrackedBranch is { } trackedBranch)
            {
                try
                {
                    var unpushedFilter = new CommitFilter
                    {
                        IncludeReachableFrom = repo.Head,
                        ExcludeReachableFrom = trackedBranch,
                        SortBy               = CommitSortStrategies.Topological
                    };
                    var unpushed  = repo.Commits.QueryBy(unpushedFilter).ToList();
                    unpushedCount = unpushed.Count;
                    unpushedMessages = unpushed.Select(FormatCommit).ToArray();

                    var unpulledFilter = new CommitFilter
                    {
                        IncludeReachableFrom = trackedBranch,
                        ExcludeReachableFrom = repo.Head,
                        SortBy               = CommitSortStrategies.Topological
                    };
                    var unpulled  = repo.Commits.QueryBy(unpulledFilter).ToList();
                    unpulledCount = unpulled.Count;
                    unpulledMessages = unpulled.Select(FormatCommit).ToArray();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"Warning: Could not determine commit divergence for {repoPath}: {ex.Message}");
                    unpulledCount = repo.Head.TrackingDetails.BehindBy;
                }
            }

            return new RepoStatus
            {
                RepoPath           = repoPath,
                RepoName           = Path.GetFileName(repoPath),
                BranchName         = repo.Head.FriendlyName,
                IsLocalOnly        = isLocalOnly,
                UncommittedCount   = uncommittedCount,
                UncommittedFiles   = uncommittedFiles,
                UncommittedEntries = uncommittedEntries,
                OldestChangeUtc    = oldestChange,
                UnpushedCount      = unpushedCount,
                UnpushedMessages   = unpushedMessages,
                UnpulledCount      = unpulledCount,
                UnpulledMessages   = unpulledMessages,
                AiSummary          = null
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to inspect {repoPath}: {ex.Message}");
            return null;
        }
    }

    private static string FormatCommit(Commit c) =>
        $"{c.Author.When:yyyy-MM-dd}  {c.Sha[..7]}  {c.MessageShort}";

    private static string StatusChar(FileStatus state) => state switch
    {
        _ when state.HasFlag(FileStatus.Conflicted)           => "!",
        _ when state.HasFlag(FileStatus.NewInIndex)           => "A",
        _ when state.HasFlag(FileStatus.DeletedFromIndex)
             | state.HasFlag(FileStatus.DeletedFromWorkdir)   => "D",
        _ when state.HasFlag(FileStatus.RenamedInIndex)
             | state.HasFlag(FileStatus.RenamedInWorkdir)     => "R",
        _ when state.HasFlag(FileStatus.NewInWorkdir)         => "?",
        _                                                      => "M",
    };
}
