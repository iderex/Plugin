using System.Reflection;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: VerifyPlugin <path-to-Moonfin.Server.dll> [expected-version]");
    return 2;
}

var assemblyPath = Path.GetFullPath(args[0]);
if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine($"Plugin assembly not found: {assemblyPath}");
    return 2;
}

var requiredResources = new[]
{
    "Moonfin.Server.Pages.configPage.html",
    "Moonfin.Server.Web.loader.js",
    "Moonfin.Server.Web.inject.html",
    "Moonfin.Server.EmulatorJS.player.html",
};

try
{
    var assembly = Assembly.LoadFrom(assemblyPath);
    if (args.Length == 2)
    {
        var actualVersion = assembly.GetName().Version?.ToString();
        if (!string.Equals(actualVersion, args[1], StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Plugin assembly version mismatch. Expected {args[1]}, found {actualVersion ?? "unknown"}.");
            return 1;
        }

        Console.WriteLine($"Verified assembly version {actualVersion}");
    }

    var availableResources = assembly.GetManifestResourceNames().ToHashSet(StringComparer.Ordinal);

    foreach (var resourceName in requiredResources)
    {
        if (!availableResources.Contains(resourceName))
        {
            Console.Error.WriteLine($"Required embedded resource is missing: {resourceName}");
            Console.Error.WriteLine("Available embedded resources:");
            foreach (var availableResource in availableResources.OrderBy(name => name, StringComparer.Ordinal))
            {
                Console.Error.WriteLine($"  {availableResource}");
            }

            return 1;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null || stream.Length == 0)
        {
            Console.Error.WriteLine($"Required embedded resource is empty or unreadable: {resourceName}");
            return 1;
        }

        Console.WriteLine($"Verified {resourceName} ({stream.Length} bytes)");
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Failed to inspect plugin assembly: {exception.Message}");
    return 1;
}

Console.WriteLine($"Verified plugin assembly: {assemblyPath}");
return 0;
