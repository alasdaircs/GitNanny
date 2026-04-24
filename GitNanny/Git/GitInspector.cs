using System.Text;
using GitNanny.Scanning;
using LibGit2Sharp;

namespace GitNanny.Git;

static class GitInspector
{
    public static RepoStatus? Inspect(string repoPath, int depth = 0)
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

            string? uncommittedDiff = null;
            if (repo.Head.Tip is { } tip && uncommittedCount > 0)
            {
                try
                {
                    var patch = repo.Diff.Compare<Patch>(
                        tip.Tree,
                        DiffTargets.Index | DiffTargets.WorkingDirectory);

                    if (patch.LinesAdded + patch.LinesDeleted > 0)
                    {
                        const int maxDiffChars = 4000;
                        var content = patch.Content;
                        uncommittedDiff = content.Length > maxDiffChars
                            ? content[..maxDiffChars] + "\n[diff truncated]"
                            : content;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"Warning: Could not generate diff for {repoPath}: {ex.Message}");
                }
            }

            var submodules = depth < 5
                ? InspectSubmodules(repo, repoPath, depth)
                : (IReadOnlyList<SubmoduleInfo>)[];

            var untrackedSnippets = new Dictionary<string, string>();
            foreach (var entry in status.Where(e => e.State.HasFlag(FileStatus.NewInWorkdir)).Take(5))
            {
                var absPath = Path.Combine(
                    repoPath,
                    entry.FilePath.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(absPath)) continue;
                try
                {
                    const int peekBytes = 512;
                    var buffer = new byte[peekBytes];
                    using var fs = File.OpenRead(absPath);
                    var bytesRead = fs.Read(buffer, 0, peekBytes);
                    var slice = buffer.AsSpan(0, bytesRead);
                    if (slice.IndexOf((byte)0) >= 0) continue;  // binary file
                    untrackedSnippets[entry.FilePath] = Encoding.UTF8.GetString(slice);
                }
                catch { /* skip unreadable files silently */ }
            }

            return new RepoStatus
            {
                RepoPath           = repoPath,
                RepoName           = Path.GetFileName(repoPath),
                BranchName         = repo.Head.FriendlyName,
                GitUserName        = repo.Config.Get<string>("user.name")?.Value,
                GitUserEmail       = repo.Config.Get<string>("user.email")?.Value,
                IsLocalOnly        = isLocalOnly,
                UncommittedCount   = uncommittedCount,
                UncommittedFiles   = uncommittedFiles,
                UncommittedEntries = uncommittedEntries,
                UncommittedDiff    = uncommittedDiff,
                UntrackedSnippets  = untrackedSnippets,
                Submodules         = submodules,
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

    private static IReadOnlyList<SubmoduleInfo> InspectSubmodules(
        Repository repo, string repoPath, int depth)
    {
        var result = new List<SubmoduleInfo>();

        foreach (var sub in repo.Submodules)
        {
            var subPath = Path.Combine(
                repoPath,
                sub.Path.Replace('/', Path.DirectorySeparatorChar));

            var isInit = Directory.Exists(subPath) && Repository.IsValid(subPath);

            RepoStatus? status = null;
            if (isInit)
            {
                status = Inspect(subPath, depth + 1);
            }

            result.Add(new SubmoduleInfo
            {
                Name          = sub.Name,
                RelativePath  = sub.Path,
                IsInitialized = isInit,
                Status        = status
            });
        }

        return result;
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
