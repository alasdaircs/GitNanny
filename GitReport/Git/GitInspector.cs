using GitReport.Scanning;
using LibGit2Sharp;

namespace GitReport.Git;

static class GitInspector
{
    public static RepoStatus? Inspect(string repoPath)
    {
        try
        {
            using var repo = new Repository(repoPath);

            var statusOptions = new StatusOptions { IncludeUntracked = true };
            var status = repo.RetrieveStatus(statusOptions);

            var uncommittedFiles = status.Select(e => e.FilePath).ToArray();
            var uncommittedCount = uncommittedFiles.Length;

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

            var isLocalOnly = repo.Head.TrackedBranch is null;
            var unpushedMessages = Array.Empty<string>();
            var unpushedCount = 0;
            int? unpulledCount = null;

            if (!isLocalOnly && repo.Head.TrackedBranch is { } trackedBranch)
            {
                try
                {
                    var commitFilter = new CommitFilter
                    {
                        IncludeReachableFrom = repo.Head,
                        ExcludeReachableFrom = trackedBranch,
                        SortBy = CommitSortStrategies.Topological
                    };
                    var unpushed = repo.Commits.QueryBy(commitFilter).ToList();
                    unpushedCount = unpushed.Count;
                    unpushedMessages = unpushed
                        .Select(c => c.MessageShort ?? "")
                        .ToArray();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"Warning: Could not determine unpushed commits for {repoPath}: {ex.Message}");
                }

                unpulledCount = repo.Head.TrackingDetails.BehindBy;
            }

            return new RepoStatus
            {
                RepoPath         = repoPath,
                RepoName         = Path.GetFileName(repoPath),
                BranchName       = repo.Head.FriendlyName,
                IsLocalOnly      = isLocalOnly,
                UncommittedCount = uncommittedCount,
                UncommittedFiles = uncommittedFiles,
                OldestChangeUtc  = oldestChange,
                UnpushedCount    = unpushedCount,
                UnpushedMessages = unpushedMessages,
                UnpulledCount    = unpulledCount,
                AiSummary        = null
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to inspect {repoPath}: {ex.Message}");
            return null;
        }
    }
}
