using GitReport.Scanning;
using MimeKit;
using System.Text;

namespace GitReport.Email;

static class ReportBuilder
{
    public static MimeMessage Build(IReadOnlyList<RepoStatus> repos, AppOptions options)
    {
        var dirtyRepos = repos
            .Where(r => r.UncommittedCount > 0 || r.UnpushedCount > 0)
            .ToList();

        var subject = dirtyRepos.Count > 0
            ? $"Git report — {dirtyRepos.Count} repo{(dirtyRepos.Count == 1 ? "" : "s")} need attention"
            : "Git report — all repos clean";

        var html = BuildHtml(dirtyRepos, repos.Count, options);

        var bodyBuilder = new BodyBuilder { HtmlBody = html };

        var message = new MimeMessage();
        message.Subject = subject;
        if (!string.IsNullOrWhiteSpace(options.RecipientAddress))
            message.To.Add(MailboxAddress.Parse(options.RecipientAddress));
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
                AppendRepoSection(sb, repo, options.NoAi);
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void AppendRepoSection(StringBuilder sb, RepoStatus repo, bool noAi)
    {
        sb.AppendLine("""<section style="background:#fff;border:1px solid #ddd;border-radius:6px;padding:16px;margin-bottom:16px;">""");

        var localBadge = repo.IsLocalOnly
            ? " <span style=\"background:#e3f2fd;color:#1565c0;border-radius:4px;padding:2px 6px;font-size:12px;font-weight:600;\">LOCAL ONLY</span>"
            : "";

        sb.AppendLine(
            $"<h2 style=\"font-size:16px;margin:0 0 8px 0;\">{Escape(repo.RepoName)}" +
            $"{localBadge}" +
            $" <span style=\"font-weight:normal;color:#666;font-size:13px;\">({Escape(repo.BranchName)})</span></h2>");

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
            sb.AppendLine(
                $"""<p style="background:#fffde7;border-left:4px solid #f9a825;padding:8px 12px;margin:0;border-radius:0 4px 4px 0;font-style:italic;">{Escape(summary)}</p>""");
        }

        sb.AppendLine("</section>");
    }

    private static string Escape(string value) =>
        value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
}
