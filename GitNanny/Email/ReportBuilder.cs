using GitNanny.Scanning;
using Markdig;
using MimeKit;
using System.Text;

namespace GitNanny.Email;

static class ReportBuilder
{
    private const int HeadTail = 5;

    private static readonly MarkdownPipeline MarkdownPipeline =
        new MarkdownPipelineBuilder().Build();

    public static MimeMessage Build(IReadOnlyList<RepoStatus> repos, AppOptions options)
    {
        var dirtyRepos = repos
            .Where(r => r.HasDirtyState)
            .ToList();

        var host = Environment.MachineName;
        var subject = dirtyRepos.Count > 0
            ? $"Git report [{host}] — {dirtyRepos.Count} repo{(dirtyRepos.Count == 1 ? "" : "s")} need attention"
            : $"Git report [{host}] — all repos clean";

        var html = BuildHtml(dirtyRepos, repos.Count, options);

        var bodyBuilder = new BodyBuilder { HtmlBody = html };

        var message = new MimeMessage();
        message.Subject = subject;
        foreach (var addr in options.RecipientAddresses.Where(a => !string.IsNullOrWhiteSpace(a)))
            message.To.Add(MailboxAddress.Parse(addr));
        message.Body = bodyBuilder.ToMessageBody();

        return message;
    }

    private static string BuildHtml(
        IReadOnlyList<RepoStatus> dirtyRepos,
        int totalScanned,
        AppOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width" />
              <title>Git Report</title>
            </head>
            <body style="font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1f1f1f;margin:0;padding:16px;background:#f8f8f8;">
              <h1 style="font-size:20px;margin-bottom:4px;">Git Report</h1>
            """);

        sb.AppendLine(
            $"<p style=\"color:#555;margin-top:0;\">Scanned {totalScanned} repo{(totalScanned == 1 ? "" : "s")}. " +
            $"Unpulled counts are as of last fetch.</p>");

        if (dirtyRepos.Count == 0)
        {
            sb.AppendLine("<p style=\"color:#2e7d32;font-weight:bold;\">All repositories are clean.</p>");
        }
        else
        {
            foreach (var repo in dirtyRepos)
                AppendRepoSection(sb, repo, options.NoAi, depth: 0);
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void AppendRepoSection(StringBuilder sb, RepoStatus repo, bool noAi, int depth)
    {
        var (sectionStyle, headingSize) = depth == 0
            ? ("background:#fff;border:1px solid #ddd;border-radius:6px;padding:16px;margin-bottom:16px;", "16px")
            : ($"background:#fafafa;border:1px solid #e0e0e0;border-left:3px solid {(repo.HasDirtyState ? "#f9a825" : "#4caf50")};border-radius:0 4px 4px 0;padding:12px;margin-bottom:10px;", "14px");

        sb.AppendLine($"""<section style="{sectionStyle}">""");

        var localBadge = repo.IsLocalOnly
            ? " <span style=\"background:#e3f2fd;color:#1565c0;border-radius:4px;padding:2px 6px;font-size:12px;font-weight:600;\">LOCAL ONLY</span>"
            : "";

        sb.AppendLine(
            $"<h2 style=\"font-size:{headingSize};margin:0 0 4px 0;\">{Escape(repo.RepoName)}" +
            $"{localBadge}" +
            $" <span style=\"font-weight:normal;color:#666;font-size:13px;\">({Escape(repo.BranchName)})</span></h2>");

        var fileUri  = new Uri(repo.RepoPath).AbsoluteUri;
        var userPart = FormatGitUser(repo.GitUserName, repo.GitUserEmail);
        var subtitle = $"""<a href="{fileUri}" style="color:#1565c0;text-decoration:none;font-family:Consolas,'Courier New',monospace;">{Escape(repo.RepoPath)}</a>""";
        if (userPart is not null)
            subtitle += $""" &nbsp;·&nbsp; <span style="color:#555;">{Escape(userPart)}</span>""";
        sb.AppendLine($"""<p style="margin:0 0 10px 0;font-size:12px;">{subtitle}</p>""");

        sb.AppendLine("""<table style="border-collapse:collapse;width:100%;margin-bottom:10px;">""");
        sb.AppendLine("""<tr style="background:#f5f5f5;">""");
        sb.AppendLine("""<th style="text-align:left;padding:6px 10px;font-size:12px;color:#555;border-bottom:1px solid #ddd;">Uncommitted</th>""");
        sb.AppendLine("""<th style="text-align:left;padding:6px 10px;font-size:12px;color:#555;border-bottom:1px solid #ddd;">Oldest change</th>""");
        sb.AppendLine("""<th style="text-align:left;padding:6px 10px;font-size:12px;color:#555;border-bottom:1px solid #ddd;">Unpushed</th>""");
        sb.AppendLine("""<th style="text-align:left;padding:6px 10px;font-size:12px;color:#555;border-bottom:1px solid #ddd;">Unpulled (last fetch)</th>""");
        sb.AppendLine("</tr><tr>");

        var oldestStr = repo.OldestChangeUtc.HasValue
            ? repo.OldestChangeUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "—";

        var unpulledStr = repo.IsLocalOnly
            ? "—"
            : (repo.UnpulledCount?.ToString() ?? "0");

        sb.AppendLine($"""<td style="padding:6px 10px;">{repo.UncommittedCount}</td>""");
        sb.AppendLine($"""<td style="padding:6px 10px;">{Escape(oldestStr)}</td>""");
        sb.AppendLine($"""<td style="padding:6px 10px;">{repo.UnpushedCount}</td>""");
        sb.AppendLine($"""<td style="padding:6px 10px;">{Escape(unpulledStr)}</td>""");
        sb.AppendLine("</tr></table>");

        if (!noAi && repo.AiSummary is { } summary)
        {
            var summaryHtml = Markdown.ToHtml(summary, MarkdownPipeline);
            sb.AppendLine(
                $"""<div style="background:#fffde7;border-left:4px solid #f9a825;padding:8px 12px;margin:0 0 8px 0;border-radius:0 4px 4px 0;font-style:italic;">{summaryHtml}</div>""");
        }

        if (repo.UncommittedCount > 0)
            AppendUncommittedFiles(sb, repo);

        if (repo.UnpushedCount > 0)
            AppendDetails(sb,
                $"{repo.UnpushedCount} unpushed commit{(repo.UnpushedCount == 1 ? "" : "s")}",
                Truncate(repo.UnpushedMessages),
                mono: true);

        if (repo.UnpulledCount > 0 && repo.UnpulledMessages.Length > 0)
            AppendDetails(sb,
                $"{repo.UnpulledCount} unpulled commit{(repo.UnpulledCount == 1 ? "" : "s")} (last fetch)",
                Truncate(repo.UnpulledMessages),
                mono: true);

        if (repo.Submodules.Count > 0)
            AppendSubmodules(sb, repo.Submodules, noAi, childDepth: depth + 1);

        sb.AppendLine("</section>");
    }

    private static void AppendSubmodules(
        StringBuilder sb, IReadOnlyList<SubmoduleInfo> submodules, bool noAi, int childDepth)
    {
        sb.AppendLine(
            """<div style="margin-top:12px;border-top:1px solid #eee;padding-top:10px;">""");
        sb.AppendLine(
            """<div style="font-size:12px;font-weight:600;color:#555;text-transform:uppercase;""" +
            """letter-spacing:0.5px;margin-bottom:8px;">Submodules</div>""");

        foreach (var sub in submodules)
        {
            if (sub.IsInitialized && sub.Status is { } status)
            {
                AppendRepoSection(sb, status, noAi, childDepth);
            }
            else
            {
                var label = sub.IsInitialized ? "error reading" : "not initialised";
                sb.AppendLine(
                    $"""<div style="border-left:3px solid #aaa;padding:6px 10px;margin-bottom:6px;""" +
                    $"""background:#fafafa;border-radius:0 4px 4px 0;font-size:13px;">""" +
                    $"""<strong>{Escape(sub.Name)}</strong> """ +
                    $"""<span style="color:#aaa;font-style:italic;">{label}</span></div>""");
            }
        }

        sb.AppendLine("</div>");
    }

    private static void AppendUncommittedFiles(StringBuilder sb, RepoStatus repo)
    {
        var label   = $"{repo.UncommittedCount} uncommitted file{(repo.UncommittedCount == 1 ? "" : "s")}";
        var entries = Truncate(repo.UncommittedEntries);

        sb.AppendLine("""<details style="margin-top:6px;">""");
        sb.AppendLine(
            $"""  <summary style="cursor:pointer;list-style:none;padding:5px 10px;""" +
            $"""background:#f0f0f0;border-radius:4px;font-size:12px;font-weight:600;""" +
            $"""color:#444;user-select:none;">&#9654; {Escape(label)}</summary>""");
        sb.AppendLine(
            """  <div style="margin-top:2px;padding:8px 10px;background:#fafafa;""" +
            """border:1px solid #e0e0e0;border-top:none;border-radius:0 0 4px 4px;""" +
            """font-size:12px;font-family:Consolas,'Courier New',monospace;">""");

        foreach (var entry in entries)
        {
            var colour = FileEntryColour(entry);
            sb.AppendLine(
                $"""    <div style="color:{colour};line-height:1.6;">{Escape(entry)}</div>""");
        }

        sb.AppendLine("  </div>");
        sb.AppendLine("</details>");
    }

    // Entry format is "[X] path/to/file" — colour by status character.
    private static string FileEntryColour(string entry) =>
        entry.Length >= 3 && entry[0] == '[' && entry[2] == ']'
            ? entry[1] switch
            {
                'A' or '?' => "#2e7d32",  // green  — added / untracked
                'D'        => "#c62828",  // red    — deleted
                _          => "#b45309",  // amber  — modified, renamed, conflicted
            }
            : "#555555";  // ellipsis / truncation lines

    // <details>/<summary> collapses in Gmail, Apple Mail, and most web clients.
    // In Outlook for Windows it degrades gracefully — content is always visible.
    private static void AppendDetails(
        StringBuilder sb, string label, IEnumerable<string> lines, bool mono)
    {
        var fontStyle = mono
            ? "font-family:Consolas,'Courier New',monospace;"
            : "";

        sb.AppendLine($"""<details style="margin-top:6px;">""");
        sb.AppendLine(
            $"""  <summary style="cursor:pointer;list-style:none;padding:5px 10px;""" +
            $"""background:#f0f0f0;border-radius:4px;font-size:12px;font-weight:600;""" +
            $"""color:#444;user-select:none;">&#9654; {Escape(label)}</summary>""");
        sb.AppendLine(
            $"""  <div style="margin-top:2px;padding:8px 10px;background:#fafafa;""" +
            $"""border:1px solid #e0e0e0;border-top:none;border-radius:0 0 4px 4px;""" +
            $"""font-size:12px;{fontStyle}">""");
        foreach (var line in lines)
            sb.AppendLine(
                $"""    <div style="line-height:1.6;">{Escape(line)}</div>""");
        sb.AppendLine("  </div>");
        sb.AppendLine("</details>");
    }

    private static IEnumerable<string> Truncate(string[] lines)
    {
        if (lines.Length <= HeadTail * 2)
            return lines;

        var omitted = lines.Length - HeadTail * 2;
        return lines.Take(HeadTail)
            .Append($"  … {omitted} more …")
            .Concat(lines.TakeLast(HeadTail));
    }

    private static string? FormatGitUser(string? name, string? email) =>
        (name, email) switch
        {
            (null, null)   => null,
            (null, var e)  => e,
            (var n, null)  => n,
            (var n, var e) => $"{n} <{e}>"
        };

    private static string Escape(string value) =>
        value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
}
