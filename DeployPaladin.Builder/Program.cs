using System.IO.Compression;
using System.Text;

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

        // Create ZIP of the payload folder in memory
        Console.WriteLine("Creating payload archive...");
        byte[] zipBytes;
        int fileCount;
        using (var zipStream = new MemoryStream())
        {
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var files = Directory.GetFiles(payloadFolder, "*", SearchOption.AllDirectories);
                fileCount = files.Length;
                foreach (var file in files)
                {
                    string relativePath = Path.GetRelativePath(payloadFolder, file).Replace('\\', '/');
                    Console.WriteLine($"  Adding: {relativePath}");
                    archive.CreateEntryFromFile(file, relativePath, CompressionLevel.Optimal);
                }
            }

            zipBytes = zipStream.ToArray();
        }

        Console.WriteLine($"Archive size: {zipBytes.Length:N0} bytes ({fileCount} files)");
        Console.WriteLine();

        // Copy base exe and append payload
        // Format: [base exe bytes] [zip bytes] [zip length as int32] [magic "PLDN"]
        Console.WriteLine("Building bundled installer...");
        File.Copy(baseExe, outputExe, overwrite: true);

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
        Console.WriteLine("  -p, --payload <folder>  Folder containing installer.lua, LICENSE.txt, and all files to install");
        Console.WriteLine("  -b, --base <exe>        Path to the published DeployPaladin installer executable");
        Console.WriteLine("  -o, --output <exe>      Path for the output bundled setup executable");
        Console.WriteLine("  -h, --help              Show this help message");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  DeployPaladin.Builder --payload .\\MyApp --base .\\DeployPaladin.exe --output .\\MyApp_Setup.exe");
    }
}
