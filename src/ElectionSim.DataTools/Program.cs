using ElectionSim.DataTools.Commands;

if (args.Length == 0)
{
    Console.WriteLine("ElectionSim DataTools - Data Pipeline");
    Console.WriteLine("Usage: dotnet run -- <command>");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  download        Download Elections Canada CSV results");
    Console.WriteLine("  scrape-338      Scrape 338Canada polling data");
    Console.WriteLine("  scrape-cbc      Scrape CBC Poll Tracker data");
    Console.WriteLine("  process         Process downloaded data into JSON");
    Console.WriteLine("  generate-sample Generate sample data for development");
    Console.WriteLine("  generate-hex-layout Generate hex cartogram layout");
    Console.WriteLine("  demographics    Download and process census demographics");
    Console.WriteLine("  validate        Run simulation accuracy validation");
    Console.WriteLine("  all             Run full pipeline");
    return;
}

var command = args[0].ToLowerInvariant();
var dataDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data"));
var rawDir = Path.Combine(dataDir, "raw");
var processedDir = Path.Combine(dataDir, "processed");
var wwwrootDataDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ElectionSim.Web", "wwwroot", "data"));

Directory.CreateDirectory(rawDir);
Directory.CreateDirectory(processedDir);
Directory.CreateDirectory(wwwrootDataDir);

switch (command)
{
    case "download":
        await DownloadCommand.RunAsync(rawDir);
        break;
    case "scrape-338":
        await Scrape338Command.RunAsync(rawDir, processedDir);
        break;
    case "scrape-cbc":
        await ScrapeCbcCommand.RunAsync(rawDir, processedDir);
        break;
    case "process":
        await ProcessCommand.RunAsync(rawDir, processedDir, wwwrootDataDir);
        break;
    case "generate-sample":
        await SampleDataGenerator.RunAsync(processedDir, wwwrootDataDir);
        break;
    case "generate-hex-layout":
        await HexLayoutGenerator.RunAsync(processedDir, wwwrootDataDir);
        break;
    case "demographics":
        await DemographicsCommand.RunAsync(rawDir, processedDir, wwwrootDataDir);
        break;
    case "validate":
        await ValidateCommand.RunAsync(dataDir);
        break;
    case "compare-demo":
        await CompareDemoCommand.RunAsync(dataDir);
        break;
    case "all":
        Console.WriteLine("Running full pipeline...");
        await DownloadCommand.RunAsync(rawDir);
        await Scrape338Command.RunAsync(rawDir, processedDir);
        await ScrapeCbcCommand.RunAsync(rawDir, processedDir);
        await ProcessCommand.RunAsync(rawDir, processedDir, wwwrootDataDir);
        Console.WriteLine("Pipeline complete.");
        break;
    default:
        Console.WriteLine($"Unknown command: {command}");
        break;
}
