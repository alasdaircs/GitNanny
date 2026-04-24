using System.Text;
using System.Text.Json;
using GitNanny.Scanning;

namespace GitNanny.Ai;

static class ClaudeSummariser
{
    private const string ApiUrl   = "https://api.anthropic.com/v1/messages";
    private const string Model    = "claude-haiku-4-5-20251001";
    private const int    MaxTokens = 250;

    public static async Task<IReadOnlyList<RepoStatus>> SummariseAsync(
        IReadOnlyList<RepoStatus> repos,
        HttpClient httpClient,
        string apiKey)
    {
        var results = new List<RepoStatus>(repos.Count);
        foreach (var repo in repos)
            results.Add(await SummariseRepoAsync(repo, httpClient, apiKey));
        return results;
    }

    private static async Task<RepoStatus> SummariseRepoAsync(
        RepoStatus repo, HttpClient httpClient, string apiKey)
    {
        // Recurse into submodules first (sequential, same as top level)
        var updatedSubmodules = new List<SubmoduleInfo>(repo.Submodules.Count);
        foreach (var sub in repo.Submodules)
        {
            if (sub.Status is { } subStatus)
                updatedSubmodules.Add(sub with { Status = await SummariseRepoAsync(subStatus, httpClient, apiKey) });
            else
                updatedSubmodules.Add(sub);
        }

        var summary = (repo.UncommittedCount > 0 || repo.UnpushedCount > 0)
            ? await CallApiAsync(repo, httpClient, apiKey)
            : null;

        return repo with { Submodules = updatedSubmodules, AiSummary = summary };
    }

    private static async Task<string?> CallApiAsync(
        RepoStatus repo, HttpClient httpClient, string apiKey)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Repository: {repo.RepoName} (branch: {repo.BranchName})");

            if (repo.UnpushedCount > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Unpushed commits:");
                foreach (var msg in repo.UnpushedMessages.Take(10))
                    sb.AppendLine($"  {msg}");
            }

            if (repo.UncommittedDiff is { } diff)
            {
                sb.AppendLine();
                sb.AppendLine("Uncommitted changes (unified diff):");
                sb.AppendLine("```");
                sb.AppendLine(diff);
                sb.AppendLine("```");
            }
            else if (repo.UncommittedCount > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Uncommitted files:");
                foreach (var f in repo.UncommittedEntries.Take(20))
                    sb.AppendLine($"  {f}");
            }

            if (repo.UntrackedSnippets.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("New (untracked) file contents:");
                foreach (var (path, snippet) in repo.UntrackedSnippets)
                {
                    sb.AppendLine($"  {path}:");
                    sb.AppendLine("  ```");
                    sb.AppendLine($"  {snippet.ReplaceLineEndings("\n  ")}");
                    sb.AppendLine("  ```");
                }
            }

            sb.AppendLine();
            sb.AppendLine("In 1–3 sentences, describe what work is in progress. Focus on the intent — what feature, fix, or task is being worked on. Do not restate file counts or commit counts; those are already shown in the report.");

            var requestBody = new
            {
                model      = Model,
                max_tokens = MaxTokens,
                system     = "You are a developer assistant reading Git repository state. Infer the purpose and intent of the work from the diff content and commit messages. Be specific and concise. Never begin with \"The developer is\" or restate facts already in the report.",
                messages   = new[] { new { role = "user", content = sb.ToString() } }
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            return doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Warning: Claude API call failed for {repo.RepoName}: {ex.Message}");
            return null;
        }
    }
}
