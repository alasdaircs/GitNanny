using System.Text;
using System.Text.Json;
using GitReport.Scanning;

namespace GitReport.Ai;

static class ClaudeSummariser
{
    private const string ApiUrl   = "https://api.anthropic.com/v1/messages";
    private const string Model    = "claude-haiku-4-5-20251001";
    private const int    MaxTokens = 150;

    public static async Task<IReadOnlyList<RepoStatus>> SummariseAsync(
        IReadOnlyList<RepoStatus> repos,
        HttpClient httpClient,
        string apiKey)
    {
        var results = new List<RepoStatus>(repos.Count);

        foreach (var repo in repos)
        {
            if (repo.UncommittedCount == 0 && repo.UnpushedCount == 0)
            {
                results.Add(repo);
                continue;
            }

            var summary = await CallApiAsync(repo, httpClient, apiKey);
            results.Add(repo with { AiSummary = summary });
        }

        return results;
    }

    private static async Task<string?> CallApiAsync(
        RepoStatus repo, HttpClient httpClient, string apiKey)
    {
        try
        {
            var fileList = string.Join(", ",
                repo.UncommittedFiles.Take(20));

            var commitList = string.Join("\n",
                repo.UnpushedMessages.Take(10));

            var prompt =
                $"""
                You are summarising the state of a Git repository for a developer's daily report.
                Repository: {repo.RepoName} (branch: {repo.BranchName})
                Uncommitted files ({repo.UncommittedCount}): {fileList}
                Unpushed commits ({repo.UnpushedCount}): {commitList}
                Summarise what work is in progress or waiting to be pushed in 1–3 plain English sentences. Be specific about what the work appears to involve.
                """;

            var requestBody = new
            {
                model      = Model,
                max_tokens = MaxTokens,
                messages   = new[] { new { role = "user", content = prompt } }
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
