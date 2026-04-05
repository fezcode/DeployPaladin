using System;
using System.IO;

namespace DeployPaladin.Core;

public class FileService
{
    private readonly BundleReader? _bundle;
    private readonly string _basePath;

    public FileService(BundleReader? bundle = null)
    {
        _bundle = bundle;
        // When not running from a bundle, resolve relative paths from the exe's directory
        _basePath = AppContext.BaseDirectory;
    }

    public void MkDir(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    public void CopyFiles(string source, string destination)
    {
        if (_bundle != null && _bundle.HasBundle)
        {
            _bundle.ExtractFile(source, destination);
            return;
        }

        // Fallback: resolve relative source from the exe's directory
        string fullSource = Path.IsPathRooted(source)
            ? source
            : Path.Combine(_basePath, source);

        if (File.Exists(fullSource))
        {
            string? destDir = Path.GetDirectoryName(destination);
            if (destDir != null && !Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            File.Copy(fullSource, destination, true);
        }
        else
        {
            throw new FileNotFoundException($"Source file not found: {fullSource}");
        }
    }
}
