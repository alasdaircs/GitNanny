using System.Net.NetworkInformation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace GitNanny.Configuration;

static class OptionsBuilder
{
    public static AppOptions BuildFromConfig()
    {
        var siblingConfigFileProvider = new PhysicalFileProvider(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "config"
            )
        );
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile( "appsettings.json", optional: true )
            .AddJsonFile( siblingConfigFileProvider, "appsettings.json", true, false )
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
