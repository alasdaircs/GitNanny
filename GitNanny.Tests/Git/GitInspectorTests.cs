using GitNanny.Git;
using LibGit2Sharp;
using Xunit;

namespace GitNanny.Tests.Git;

public sealed class GitInspectorTests : IDisposable
{
    private readonly string _root;

    public GitInspectorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"gitnanny-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => ForceDelete(_root);

    // ── clean repo ──────────────────────────────────────────────────────────

    [Fact]
    public void CleanRepo_AfterCommit_ReturnsZeroCounts()
    {
        var repo = InitRepo();

        Touch("tracked.cs", "hello");
        Stage(repo, "tracked.cs");
        Commit(repo, "Initial");

        var result = GitInspector.Inspect(_root);

        Assert.NotNull(result);
        Assert.Equal(0, result.UncommittedCount);
        Assert.Equal(0, result.UnpushedCount);
        Assert.True(result.IsLocalOnly);
    }

    // ── untracked files ─────────────────────────────────────────────────────

    [Fact]
    public void UntrackedFile_IsIncluded()
    {
        var repo = InitRepo();
        Touch("tracked.cs", "hello");
        Stage(repo, "tracked.cs");
        Commit(repo, "Initial");

        Touch("new.cs", "// new");

        var result = GitInspector.Inspect(_root);

        Assert.Equal(1, result!.UncommittedCount);
        Assert.Contains("new.cs", result.UncommittedFiles);
    }

    // ── .gitignore enforcement ──────────────────────────────────────────────

    [Fact]
    public void RootGitIgnore_ExcludesMatchingFiles()
    {
        var repo = InitRepo();
        Touch(".gitignore", "*.log");
        Stage(repo, ".gitignore");
        Commit(repo, "Add .gitignore");

        Touch("debug.log", "noise");

        var result = GitInspector.Inspect(_root);

        Assert.Equal(0, result!.UncommittedCount);
        Assert.DoesNotContain("debug.log", result.UncommittedFiles);
    }

    [Fact]
    public void SubdirectoryGitIgnore_ExcludesMatchingFiles()
    {
        // sub/.gitignore is committed; a new file matching its pattern must be ignored.
        var repo = InitRepo();
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        Touch("sub/.gitignore", "*.tmp");
        Stage(repo, "sub/.gitignore");
        Commit(repo, "Initial with sub/.gitignore");

        Touch("sub/scratch.tmp", "temp");

        var result = GitInspector.Inspect(_root);

        Assert.DoesNotContain("sub/scratch.tmp", result!.UncommittedFiles);
    }

    // ── modified tracked files ──────────────────────────────────────────────

    [Fact]
    public void ModifiedTrackedFile_IsIncluded()
    {
        var repo = InitRepo();
        Touch("tracked.cs", "original");
        Stage(repo, "tracked.cs");
        Commit(repo, "Initial");

        Touch("tracked.cs", "modified");

        var result = GitInspector.Inspect(_root);

        Assert.Equal(1, result!.UncommittedCount);
        Assert.Contains("tracked.cs", result.UncommittedFiles);
    }

    [Fact]
    public void StagedFile_IsIncluded()
    {
        var repo = InitRepo();
        Touch("tracked.cs", "original");
        Stage(repo, "tracked.cs");
        Commit(repo, "Initial");

        Touch("staged.cs", "// staged");
        Stage(repo, "staged.cs");

        var result = GitInspector.Inspect(_root);

        Assert.Equal(1, result!.UncommittedCount);
        Assert.Contains("staged.cs", result.UncommittedFiles);
    }

    // ── error handling ──────────────────────────────────────────────────────

    [Fact]
    public void NonExistentPath_ReturnsNull()
    {
        var result = GitInspector.Inspect(Path.Combine(_root, "does-not-exist"));
        Assert.Null(result);
    }

    [Fact]
    public void PlainDirectory_NotARepo_ReturnsNull()
    {
        var plain = Path.Combine(_root, "notarepo");
        Directory.CreateDirectory(plain);

        var result = GitInspector.Inspect(plain);

        Assert.Null(result);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private Repository InitRepo()
    {
        Repository.Init(_root);
        return new Repository(_root);
    }

    private Signature Sig() =>
        new("Test User", "test@example.com", DateTimeOffset.UtcNow);

    private void Touch(string relative, string content)
    {
        var full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static void Stage(Repository repo, string path) =>
        Commands.Stage(repo, path);

    private void Commit(Repository repo, string message)
    {
        var sig = Sig();
        repo.Commit(message, sig, sig, new CommitOptions { AllowEmptyCommit = true });
    }

    private static void ForceDelete(string path)
    {
        if (!Directory.Exists(path)) return;
        // LibGit2Sharp marks pack files read-only; clear before deleting.
        foreach (var f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(path, recursive: true);
    }
}
