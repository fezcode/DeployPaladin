using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DeployPaladin.Core;

public class BundleReader : IDisposable
{
    private readonly MemoryStream? _zipStream;
    private readonly ZipArchive? _archive;

    public bool HasBundle => _archive != null;

    public BundleReader()
    {
        string exePath = Environment.ProcessPath ?? string.Empty;
        if (!File.Exists(exePath)) return;

        using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (fs.Length < 8) return;

        // Check for magic "PLDN" at the end
        fs.Seek(-4, SeekOrigin.End);
        byte[] magicBytes = new byte[4];
        fs.ReadExactly(magicBytes, 0, 4);
        string magic = Encoding.ASCII.GetString(magicBytes);

        if (magic != "PLDN") return;

        // Read zip length (4 bytes before the magic)
        fs.Seek(-8, SeekOrigin.End);
        byte[] lengthBytes = new byte[4];
        fs.ReadExactly(lengthBytes, 0, 4);
        int zipLength = BitConverter.ToInt32(lengthBytes, 0);

        if (zipLength <= 0 || zipLength > fs.Length - 8) return;

        // Read zip data
        fs.Seek(-8 - zipLength, SeekOrigin.End);
        byte[] zipBytes = new byte[zipLength];
        fs.ReadExactly(zipBytes, 0, zipLength);

        _zipStream = new MemoryStream(zipBytes);
        _archive = new ZipArchive(_zipStream, ZipArchiveMode.Read);
    }

    public string ReadTextFile(string path)
    {
        if (_archive == null) return string.Empty;

        var entry = _archive.GetEntry(path.Replace("\\", "/"));
        if (entry == null) return string.Empty;

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void ExtractFile(string sourceInZip, string destOnDisk)
    {
        if (_archive == null) return;

        var entry = _archive.GetEntry(sourceInZip.Replace("\\", "/"));
        if (entry == null)
            throw new FileNotFoundException($"File not found in bundle: {sourceInZip}");

        string? destDir = Path.GetDirectoryName(destOnDisk);
        if (destDir != null && !Directory.Exists(destDir))
            Directory.CreateDirectory(destDir);

        entry.ExtractToFile(destOnDisk, overwrite: true);
    }

    public void ExtractDirectory(string sourceDirInZip, string destDirOnDisk)
    {
        if (_archive == null) return;

        string prefix = sourceDirInZip.Replace("\\", "/").TrimEnd('/') + "/";

        foreach (var entry in _archive.Entries)
        {
            string entryPath = entry.FullName.Replace("\\", "/");
            if (!entryPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip directory entries
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            string relativePath = entryPath.Substring(prefix.Length);
            string destPath = Path.Combine(destDirOnDisk, relativePath.Replace("/", Path.DirectorySeparatorChar.ToString()));

            string? destDir = Path.GetDirectoryName(destPath);
            if (destDir != null && !Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            entry.ExtractToFile(destPath, overwrite: true);
        }
    }

    public void Dispose()
    {
        _archive?.Dispose();
        _zipStream?.Dispose();
    }
}
