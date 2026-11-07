using System.Text.Json;
using SensorAnalysis.Distribution;

namespace SensorAnalysis.Distribution.Tool;

/// <summary>Command line for the product release planner. It does not publish anything.</summary>
public static class Program
{
    /// <summary>Entry point. No arguments prints help.</summary>
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Any(IsHelp))
            {
                Console.WriteLine(Help);
                return 0;
            }

            return args[0] switch
            {
                "plan" => Plan(args.Skip(1).ToArray()),
                "previous" => Previous(args.Skip(1).ToArray()),
                _ => Fail($"Unknown command '{args[0]}'.")
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or IOException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static int Plan(string[] args)
    {
        var previousPath = RequireOption(args, "--previous");
        var currentPath = RequireOption(args, "--current");
        var previous = ProductCatalog.Load(previousPath);
        var current = ProductCatalog.Load(currentPath)
            ?? throw new InvalidDataException("The current manifest is missing.");
        var releases = ReleasePlanner.Plan(previous, current);
        Console.WriteLine(JsonSerializer.Serialize(releases, ProductCatalog.JsonOptions));
        return 0;
    }

    private static int Previous(string[] args)
    {
        var releasesPath = RequireOption(args, "--releases");
        var productId = RequireOption(args, "--id");
        var version = RequireOption(args, "--version");
        var text = File.ReadAllText(releasesPath).Trim().TrimStart('\uFEFF');
        var releases = JsonSerializer.Deserialize<List<PublishedRelease>>(text, ProductCatalog.JsonOptions)
            ?? throw new InvalidDataException("The release list is empty.");
        var tag = ReleasePlanner.PreviousStableTag(releases, productId, version);
        if (!string.IsNullOrEmpty(tag))
            Console.WriteLine(tag);
        return 0;
    }

    private static string RequireOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
                return args[i + 1];
        }

        throw new InvalidDataException($"Missing {name}.");
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine(Help);
        return 1;
    }

    private static bool IsHelp(string arg) =>
        arg is "-h" or "--help" or "/?" or "-?";

    private const string Help = """
        ReleasePlan decides which SensorAnalysis products a commit should publish.

        The tool reads the product manifest and optional GitHub release metadata.
        It does not create tags, releases, or packages, and it needs no secrets.

        Usage:
          ReleasePlan plan --previous <file> --current <file>
          ReleasePlan previous --releases <file> --id <product-id> --version <X.Y.Z>
          ReleasePlan --help

        plan writes a JSON array to standard output. An empty array means do not publish.
        Pass a file whose contents are null as --previous when the manifest is new.
        previous writes the older stable tag for that product, or writes nothing.

        Example:
          dotnet run --project distribution/ReleasePlan -- plan --previous previous.json --current distribution/products.json
        """;
}
