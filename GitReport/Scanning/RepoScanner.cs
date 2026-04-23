using Microsoft.Extensions.FileSystemGlobbing;

namespace GitReport.Scanning;

static class RepoScanner
{
    public static IReadOnlyList<string> Discover(AppOptions options)
    {
        var matcher = new Matcher();
        foreach (var pattern in options.ExcludePatterns)
            matcher.AddInclude(pattern);

        var repos = new List<string>();

        foreach (var root in options.ScanRoots)
        {
            if (!Directory.Exists(root))
            {
                Console.Error.WriteLine($"Warning: Scan root does not exist: {root}");
                continue;
            }

            ScanDirectory(root, 0, options.MaxDepth, matcher, repos, options.Verbose);
        }

        return repos;
    }

    private static void ScanDirectory(
        string path, int depth, int maxDepth,
        Matcher excludeMatcher, List<string> repos, bool verbose)
    {
        if (depth > maxDepth)
            return;

        if (verbose)
            Console.Out.WriteLine($"  Entering: {path}");

        if (Directory.Exists(Path.Combine(path, ".git")))
        {
            if (verbose)
                Console.Out.WriteLine($"  Found repo: {path}");
            repos.Add(path);
            return;
        }

        try
        {
            foreach (var subdir in Directory.EnumerateDirectories(path))
            {
                var name = Path.GetFileName(subdir);
                if (excludeMatcher.Match(name).HasMatches)
                    continue;

                ScanDirectory(subdir, depth + 1, maxDepth, excludeMatcher, repos, verbose);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"Warning: Cannot access {path}: {ex.Message}");
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Warning: IO error at {path}: {ex.Message}");
        }
    }
}
