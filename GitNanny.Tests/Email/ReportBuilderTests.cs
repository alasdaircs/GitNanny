using GitNanny.Email;
using GitNanny.Scanning;
using MimeKit;
using Xunit;

namespace GitNanny.Tests.Email;

public class ReportBuilderTests
{
    // ── helpers ─────────────────────────────────────────────────────────────

    private static AppOptions Opts(bool noAi = true) => new()
    {
        RecipientAddresses = ["test@example.com"],
        NoAi               = noAi
    };

    private static RepoStatus Clean(string name = "Repo") => new()
    {
        RepoPath   = $@"C:\Repos\{name}",
        RepoName   = name,
        BranchName = "main"
    };

    private static RepoStatus Dirty(
        string   name    = "Repo",
        string[]? entries = null,
        int?     count   = null) => new()
    {
        RepoPath           = $@"C:\Repos\{name}",
        RepoName           = name,
        BranchName         = "main",
        UncommittedEntries = entries ?? ["[M] file.cs"],
        UncommittedFiles   = ["file.cs"],
        UncommittedCount   = count ?? entries?.Length ?? 1
    };

    // ── subject line ────────────────────────────────────────────────────────

    [Fact]
    public void Subject_AllClean_SaysAllClean()
    {
        var msg = ReportBuilder.Build([Clean()], Opts());
        Assert.Contains("all repos clean", msg.Subject);
    }

    [Fact]
    public void Subject_OneDirtyRepo_UsesSingular()
    {
        var msg = ReportBuilder.Build([Dirty()], Opts());
        Assert.Contains("1 repo", msg.Subject);
        Assert.DoesNotContain("repos", msg.Subject);
    }

    [Fact]
    public void Subject_TwoDirtyRepos_UsesPlural()
    {
        var msg = ReportBuilder.Build([Dirty("A"), Dirty("B")], Opts());
        Assert.Contains("2 repos", msg.Subject);
    }

    // ── file colour coding ──────────────────────────────────────────────────

    [Theory]
    [InlineData("[A] New.cs")]
    [InlineData("[?] Untracked.cs")]
    public void AddedOrUntrackedFile_RenderedGreen(string entry)
    {
        var msg = ReportBuilder.Build([Dirty(entries: [entry])], Opts());
        Assert.Contains("#2e7d32", msg.HtmlBody);
    }

    [Fact]
    public void DeletedFile_RenderedRed()
    {
        var msg = ReportBuilder.Build([Dirty(entries: ["[D] Gone.cs"])], Opts());
        Assert.Contains("#c62828", msg.HtmlBody);
    }

    [Theory]
    [InlineData("[M] Changed.cs")]
    [InlineData("[R] Renamed.cs")]
    [InlineData("[!] Conflict.cs")]
    public void ModifiedOrRenamedOrConflicted_RenderedAmber(string entry)
    {
        var msg = ReportBuilder.Build([Dirty(entries: [entry])], Opts());
        Assert.Contains("#b45309", msg.HtmlBody);
    }

    // ── AI summary ──────────────────────────────────────────────────────────

    [Fact]
    public void AiSummary_Markdown_RenderedAsHtml()
    {
        var repo = Dirty() with { AiSummary = "Work is **in progress**." };
        var msg  = ReportBuilder.Build([repo], Opts(noAi: false));

        Assert.Contains("<strong>in progress</strong>", msg.HtmlBody);
        Assert.DoesNotContain("**in progress**", msg.HtmlBody);
    }

    [Fact]
    public void AiSummary_WhenNoAi_NotIncluded()
    {
        var repo = Dirty() with { AiSummary = "should-not-appear" };
        var msg  = ReportBuilder.Build([repo], Opts(noAi: true));

        Assert.DoesNotContain("should-not-appear", msg.HtmlBody);
    }

    // ── truncation ──────────────────────────────────────────────────────────

    [Fact]
    public void UncommittedFiles_MoreThanTen_Truncated()
    {
        var entries = Enumerable.Range(1, 15).Select(i => $"[M] file{i:D2}.cs").ToArray();
        var repo    = Dirty(entries: entries, count: 15);
        var msg     = ReportBuilder.Build([repo], Opts());

        Assert.Contains("file01.cs", msg.HtmlBody);   // head preserved
        Assert.Contains("file15.cs", msg.HtmlBody);   // tail preserved
        Assert.Contains("more",      msg.HtmlBody);   // ellipsis line
    }

    [Fact]
    public void UncommittedFiles_TenOrFewer_NotTruncated()
    {
        var entries = Enumerable.Range(1, 10).Select(i => $"[M] file{i:D2}.cs").ToArray();
        var repo    = Dirty(entries: entries, count: 10);
        var msg     = ReportBuilder.Build([repo], Opts());

        Assert.DoesNotContain("more", msg.HtmlBody);
    }

    // ── badges and metadata ─────────────────────────────────────────────────

    [Fact]
    public void LocalOnlyRepo_ShowsLocalOnlyBadge()
    {
        var repo = Dirty() with { IsLocalOnly = true };
        var msg  = ReportBuilder.Build([repo], Opts());
        Assert.Contains("LOCAL ONLY", msg.HtmlBody);
    }

    [Fact]
    public void UnpushedCommits_RenderedInSection()
    {
        var repo = new RepoStatus
        {
            RepoPath         = @"C:\Repos\Test",
            RepoName         = "Test",
            BranchName       = "main",
            UnpushedCount    = 2,
            UnpushedMessages = ["2026-01-01  abc1234  First commit", "2026-01-02  def5678  Second commit"]
        };
        var msg = ReportBuilder.Build([repo], Opts());

        Assert.Contains("2 unpushed commits", msg.HtmlBody);
        Assert.Contains("First commit",       msg.HtmlBody);
        Assert.Contains("Second commit",      msg.HtmlBody);
    }

    // ── HTML escaping ───────────────────────────────────────────────────────

    [Fact]
    public void RepoName_SpecialChars_AreHtmlEscaped()
    {
        var msg = ReportBuilder.Build([Dirty("<evil>&amp;repo")], Opts());

        Assert.DoesNotContain("<evil>",         msg.HtmlBody);
        Assert.Contains("&lt;evil&gt;",         msg.HtmlBody);
    }

    [Fact]
    public void FilePath_SpecialChars_AreHtmlEscaped()
    {
        var msg = ReportBuilder.Build([Dirty(entries: ["[M] src/<gen>.cs"])], Opts());

        Assert.DoesNotContain("<gen>",  msg.HtmlBody);
        Assert.Contains("&lt;gen&gt;", msg.HtmlBody);
    }

    // ── path and git user ───────────────────────────────────────────────────

    [Fact]
    public void RepoPath_RenderedAsFileLink()
    {
        var msg = ReportBuilder.Build([Dirty()], Opts());

        Assert.Contains("file:///",       msg.HtmlBody);
        Assert.Contains(@"C:\Repos\Repo", msg.HtmlBody);
    }

    [Fact]
    public void GitUser_NameAndEmail_RenderedTogether()
    {
        var repo = Dirty() with { GitUserName = "Alice", GitUserEmail = "alice@example.com" };
        var msg  = ReportBuilder.Build([repo], Opts());

        Assert.Contains("Alice &lt;alice@example.com&gt;", msg.HtmlBody);
    }

    [Fact]
    public void GitUser_EmailOnly_RenderedWithoutAngleBrackets()
    {
        var repo = Dirty() with { GitUserName = null, GitUserEmail = "bob@example.com" };
        var msg  = ReportBuilder.Build([repo], Opts());

        Assert.Contains("bob@example.com", msg.HtmlBody);
        Assert.DoesNotContain("&lt;bob",   msg.HtmlBody);
    }

    [Fact]
    public void GitUser_Neither_SubtitleOmitsUserPart()
    {
        var repo = Dirty() with { GitUserName = null, GitUserEmail = null };
        var msg  = ReportBuilder.Build([repo], Opts());

        Assert.DoesNotContain("·", msg.HtmlBody);
    }

    // ── multiple recipients ─────────────────────────────────────────────────

    [Fact]
    public void MultipleRecipients_AllAddedToMessage()
    {
        var opts = new AppOptions
        {
            RecipientAddresses = ["a@example.com", "b@example.com", "c@example.com"],
            NoAi = true
        };
        var msg = ReportBuilder.Build([Dirty()], opts);

        var addresses = msg.To.OfType<MailboxAddress>().Select(m => m.Address).ToList();
        Assert.Equal(3, addresses.Count);
        Assert.Contains("a@example.com", addresses);
        Assert.Contains("b@example.com", addresses);
        Assert.Contains("c@example.com", addresses);
    }
}
