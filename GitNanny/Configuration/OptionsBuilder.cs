using Microsoft.Extensions.Configuration;

namespace GitNanny.Configuration;

static class OptionsBuilder
{
    public static AppOptions BuildFromConfig()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile( "appsettings.json", optional: true )
            .AddJsonFile( "..\\config\\appsettings.json", optional: true )
            .AddEnvironmentVariables()
            .Build();

        return new AppOptions
        {
            ScanRoots        = config.GetSection("ScanRoots").Get<string[]>() ?? [],
            ExcludePatterns  = config.GetSection("ExcludePatterns").Get<string[]>()
                                 ?? ["bin", "obj", "node_modules", ".git"],
            MaxDepth         = config.GetValue<int>("MaxDepth", 5),
            SkipCleanRepos   = config.GetValue<bool>("SkipCleanRepos", true),
            AzureClientId    = config.GetValue<string>("AzureClientId") ?? "",
            RecipientAddress = config.GetValue<string>("RecipientAddress") ?? "",
        };
    }
}
