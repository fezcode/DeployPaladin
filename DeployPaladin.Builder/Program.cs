using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace DeployPaladin.Builder;

class Program
{
    static int Main(string[] args)
    {
        string? payloadFolder = null;
        string? baseExe = null;
        string? outputExe = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--payload" or "-p":
                    if (++i < args.Length) payloadFolder = args[i];
                    break;
                case "--base" or "-b":
                    if (++i < args.Length) baseExe = args[i];
                    break;
                case "--output" or "-o":
                    if (++i < args.Length) outputExe = args[i];
                    break;
                case "--help" or "-h":
                    PrintHelp();
                    return 0;
            }
        }

        if (payloadFolder == null || baseExe == null || outputExe == null)
        {
            PrintHelp();
            return 1;
        }

        payloadFolder = Path.GetFullPath(payloadFolder);
        baseExe = Path.GetFullPath(baseExe);
        outputExe = Path.GetFullPath(outputExe);

        if (!Directory.Exists(payloadFolder))
        {
            Console.Error.WriteLine($"Error: Payload folder not found: {payloadFolder}");
            return 1;
        }

        if (!File.Exists(baseExe))
        {
            Console.Error.WriteLine($"Error: Base installer executable not found: {baseExe}");
            return 1;
        }

        string luaPath = Path.Combine(payloadFolder, "installer.lua");
        if (!File.Exists(luaPath))
        {
            Console.Error.WriteLine($"Error: installer.lua not found in payload folder: {payloadFolder}");
            return 1;
        }

        string? outputDir = Path.GetDirectoryName(outputExe);
        if (outputDir != null && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        Console.WriteLine($"Payload : {payloadFolder}");
        Console.WriteLine($"Base    : {baseExe}");
        Console.WriteLine($"Output  : {outputExe}");
        Console.WriteLine();

        // Parse installer.lua to discover referenced files and directories
        Console.WriteLine("Parsing installer.lua for referenced files...");
        string luaContent = File.ReadAllText(luaPath);

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "installer.lua" };
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // CopyFiles("source", ...) — first argument is the payload source file
        foreach (Match m in Regex.Matches(luaContent, @"CopyFiles\s*\(\s*""([^""]+)"""))
            files.Add(m.Groups[1].Value);

        // CopyDir("sourceDir", ...) — first argument is the payload source directory
        foreach (Match m in Regex.Matches(luaContent, @"CopyDir\s*\(\s*""([^""]+)"""))
            dirs.Add(m.Groups[1].Value);

        // contentFile = "..." (e.g., LICENSE.txt)
        foreach (Match m in Regex.Matches(luaContent, @"contentFile\s*=\s*""([^""]+)"""))
            files.Add(m.Groups[1].Value);

        // SetAppIcon("...")
        foreach (Match m in Regex.Matches(luaContent, @"SetAppIcon\s*\(\s*""([^""]+)"""))
            files.Add(m.Groups[1].Value);

        // icon = "..." inside shortcut options (only literal payload paths, not %INSTALLDIR% refs)
        foreach (Match m in Regex.Matches(luaContent, @"icon\s*=\s*""([^""]+)"""))
        {
            string val = m.Groups[1].Value;
            if (!val.Contains('%'))
                files.Add(val);
        }

        // Image APIs: SetLeftPaneImage, SetBackgroundImage, SetTopPaneImage
        foreach (Match m in Regex.Matches(luaContent, @"Set(?:LeftPane|Background|TopPane)Image\s*\(\s*""([^""]+)"""))
            files.Add(m.Groups[1].Value);

        // Create ZIP of only the referenced files
        Console.WriteLine("Creating payload archive...");
        byte[] zipBytes;
        int fileCount = 0;
        var missing = new List<string>();

        using (var zipStream = new MemoryStream())
        {
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                // Add individual files
                foreach (var relPath in files)
                {
                    string fullPath = Path.Combine(payloadFolder, relPath.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(fullPath))
                    {
                        missing.Add(relPath);
                        continue;
                    }

                    string entryName = relPath.Replace('\\', '/');
                    Console.WriteLine($"  Adding: {entryName}");
                    archive.CreateEntryFromFile(fullPath, entryName, CompressionLevel.Optimal);
                    fileCount++;
                }

                // Add directories recursively
                foreach (var relDir in dirs)
                {
                    string fullDir = Path.Combine(payloadFolder, relDir.Replace('/', Path.DirectorySeparatorChar));
                    if (!Directory.Exists(fullDir))
                    {
                        missing.Add(relDir + "/");
                        continue;
                    }

                    foreach (var file in Directory.GetFiles(fullDir, "*", SearchOption.AllDirectories))
                    {
                        string entryName = Path.GetRelativePath(payloadFolder, file).Replace('\\', '/');
                        Console.WriteLine($"  Adding: {entryName}");
                        archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                        fileCount++;
                    }
                }
            }

            zipBytes = zipStream.ToArray();
        }

        if (missing.Count > 0)
        {
            Console.WriteLine();
            foreach (var m in missing)
                Console.Error.WriteLine($"Warning: Referenced path not found in payload: {m}");
        }

        Console.WriteLine($"Archive size: {zipBytes.Length:N0} bytes ({fileCount} files)");
        Console.WriteLine();

        // Copy base exe and append payload
        // Format: [base exe bytes] [zip bytes] [zip length as int32] [magic "PLDN"]
        Console.WriteLine("Building bundled installer...");
        File.Copy(baseExe, outputExe, overwrite: true);

        // Patch the exe icon if SetAppIcon is specified
        string? appIconPath = null;
        var iconMatch = Regex.Match(luaContent, @"SetAppIcon\s*\(\s*""([^""]+)""");
        if (iconMatch.Success)
        {
            string iconRelPath = iconMatch.Groups[1].Value;
            string iconFullPath = Path.Combine(payloadFolder, iconRelPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(iconFullPath))
            {
                Console.WriteLine($"Patching exe icon: {iconRelPath}");
                if (!IconPatcher.PatchIcon(outputExe, iconFullPath))
                {
                    Console.Error.WriteLine("Warning: Failed to patch exe icon, continuing with default icon.");
                }
            }
            else
            {
                Console.Error.WriteLine($"Warning: Icon file not found: {iconRelPath}");
            }
        }

        using (var fs = new FileStream(outputExe, FileMode.Append, FileAccess.Write))
        {
            fs.Write(zipBytes, 0, zipBytes.Length);
            fs.Write(BitConverter.GetBytes(zipBytes.Length), 0, 4);
            fs.Write(Encoding.ASCII.GetBytes("PLDN"), 0, 4);
        }

        var outputInfo = new FileInfo(outputExe);
        Console.WriteLine($"Done! Output: {outputExe} ({outputInfo.Length:N0} bytes)");
        return 0;
    }

    static void PrintHelp()
    {
        Console.WriteLine("Deploy Paladin Builder - Bundle a payload into a self-extracting installer");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  DeployPaladin.Builder --payload <folder> --base <exe> --output <exe>");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -p, --payload <folder>  Folder containing installer.lua and referenced payload files");
        Console.WriteLine("  -b, --base <exe>        Path to the published DeployPaladin installer executable");
        Console.WriteLine("  -o, --output <exe>      Path for the output bundled setup executable");
        Console.WriteLine("  -h, --help              Show this help message");
        Console.WriteLine();
        Console.WriteLine("The builder parses installer.lua and bundles only the files referenced by");
        Console.WriteLine("CopyFiles, CopyDir, contentFile, SetAppIcon, and image instructions.");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  DeployPaladin.Builder --payload .\\MyApp --base .\\DeployPaladin.exe --output .\\MyApp_Setup.exe");
    }
}
