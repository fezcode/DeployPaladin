using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace DeployPaladin.Builder;

/// <summary>
/// Replaces the main icon in a PE executable using Win32 resource APIs.
/// Handles .NET single-file/self-contained apps by preserving the appended
/// bundle data that EndUpdateResource would otherwise truncate.
/// </summary>
static class IconPatcher
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr BeginUpdateResource(string pFileName, bool bDeleteExistingResources);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateResource(IntPtr hUpdate, IntPtr lpType, IntPtr lpName,
        ushort wLanguage, byte[] lpData, uint cb);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EndUpdateResource(IntPtr hUpdate, bool fDiscard);

    private static IntPtr RT_ICON => (IntPtr)3;
    private static IntPtr RT_GROUP_ICON => (IntPtr)14;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct IconDir
    {
        public ushort Reserved;
        public ushort Type;
        public ushort Count;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct IconDirEntry
    {
        public byte Width;
        public byte Height;
        public byte ColorCount;
        public byte Reserved;
        public ushort Planes;
        public ushort BitCount;
        public uint BytesInRes;
        public uint ImageOffset;
    }

    public static bool PatchIcon(string exePath, string icoPath)
    {
        byte[] icoBytes = File.ReadAllBytes(icoPath);

        if (icoBytes.Length < 6)
        {
            Console.Error.WriteLine("Error: Invalid .ico file (too small)");
            return false;
        }

        var dir = MemRead<IconDir>(icoBytes, 0);
        if (dir.Type != 1 || dir.Count == 0)
        {
            Console.Error.WriteLine("Error: Invalid .ico file (bad header)");
            return false;
        }

        var entries = new IconDirEntry[dir.Count];
        for (int i = 0; i < dir.Count; i++)
            entries[i] = MemRead<IconDirEntry>(icoBytes, 6 + i * 16);

        // Find the end of PE sections — everything after is .NET bundle data
        // that EndUpdateResource would truncate.
        long peEnd = FindPeEnd(exePath);
        byte[]? tailData = null;

        if (peEnd > 0)
        {
            byte[] allBytes = File.ReadAllBytes(exePath);
            if (peEnd < allBytes.Length)
            {
                tailData = new byte[allBytes.Length - peEnd];
                Array.Copy(allBytes, peEnd, tailData, 0, tailData.Length);
            }
        }

        // Perform the resource update
        IntPtr hUpdate = BeginUpdateResource(exePath, false);
        if (hUpdate == IntPtr.Zero)
        {
            Console.Error.WriteLine($"Error: BeginUpdateResource failed (0x{Marshal.GetLastWin32Error():X8})");
            return false;
        }

        for (int i = 0; i < dir.Count; i++)
        {
            byte[] imageData = new byte[entries[i].BytesInRes];
            Array.Copy(icoBytes, entries[i].ImageOffset, imageData, 0, imageData.Length);

            ushort iconId = (ushort)(i + 1);
            if (!UpdateResource(hUpdate, RT_ICON, (IntPtr)iconId, 0, imageData, (uint)imageData.Length))
            {
                Console.Error.WriteLine($"Error: UpdateResource RT_ICON #{iconId} failed");
                EndUpdateResource(hUpdate, true);
                return false;
            }
        }

        // Build RT_GROUP_ICON directory
        int grpSize = 6 + dir.Count * 14;
        byte[] grpData = new byte[grpSize];
        BitConverter.GetBytes(dir.Reserved).CopyTo(grpData, 0);
        BitConverter.GetBytes(dir.Type).CopyTo(grpData, 2);
        BitConverter.GetBytes(dir.Count).CopyTo(grpData, 4);

        for (int i = 0; i < dir.Count; i++)
        {
            int off = 6 + i * 14;
            grpData[off + 0] = entries[i].Width;
            grpData[off + 1] = entries[i].Height;
            grpData[off + 2] = entries[i].ColorCount;
            grpData[off + 3] = entries[i].Reserved;
            BitConverter.GetBytes(entries[i].Planes).CopyTo(grpData, off + 4);
            BitConverter.GetBytes(entries[i].BitCount).CopyTo(grpData, off + 6);
            BitConverter.GetBytes(entries[i].BytesInRes).CopyTo(grpData, off + 8);
            BitConverter.GetBytes((ushort)(i + 1)).CopyTo(grpData, off + 12);
        }

        if (!UpdateResource(hUpdate, RT_GROUP_ICON, (IntPtr)1, 0, grpData, (uint)grpData.Length))
        {
            Console.Error.WriteLine("Error: UpdateResource RT_GROUP_ICON failed");
            EndUpdateResource(hUpdate, true);
            return false;
        }

        if (!EndUpdateResource(hUpdate, false))
        {
            Console.Error.WriteLine($"Error: EndUpdateResource failed (0x{Marshal.GetLastWin32Error():X8})");
            return false;
        }

        // Re-append the .NET bundle data that EndUpdateResource truncated
        if (tailData != null && tailData.Length > 0)
        {
            // The .NET apphost stores the bundle offset at a known signature.
            // After EndUpdateResource, the PE size may have changed, so we need
            // to update the bundle offset pointer inside the apphost.
            byte[] patchedPe = File.ReadAllBytes(exePath);
            long newPeEnd = patchedPe.Length;
            long oldPeEnd = peEnd;

            using var fs = new FileStream(exePath, FileMode.Append, FileAccess.Write);
            fs.Write(tailData, 0, tailData.Length);
            fs.Flush();

            // Patch the .NET bundle header offset if it shifted
            if (newPeEnd != oldPeEnd)
            {
                PatchBundleOffset(exePath, oldPeEnd, newPeEnd);
            }
        }

        return true;
    }

    /// <summary>
    /// Finds the end of PE sections (where appended data like .NET bundle begins).
    /// </summary>
    private static long FindPeEnd(string exePath)
    {
        try
        {
            using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);

            // DOS header: e_lfanew at offset 0x3C
            if (fs.Length < 0x40) return -1;
            fs.Seek(0x3C, SeekOrigin.Begin);
            int peOffset = br.ReadInt32();

            // PE signature
            fs.Seek(peOffset, SeekOrigin.Begin);
            if (br.ReadUInt32() != 0x00004550) return -1; // "PE\0\0"

            // COFF header
            fs.Seek(2, SeekOrigin.Current); // Machine
            ushort numberOfSections = br.ReadUInt16();
            fs.Seek(12, SeekOrigin.Current); // skip TimeDateStamp, PointerToSymbolTable, NumberOfSymbols
            ushort sizeOfOptionalHeader = br.ReadUInt16();
            // skip Characteristics
            fs.Seek(2, SeekOrigin.Current);

            // Skip optional header
            fs.Seek(sizeOfOptionalHeader, SeekOrigin.Current);

            // Read section headers to find the furthest extent
            long maxEnd = 0;
            for (int i = 0; i < numberOfSections; i++)
            {
                fs.Seek(16, SeekOrigin.Current); // Name(8) + VirtualSize(4) + VirtualAddress(4)
                uint sizeOfRawData = br.ReadUInt32();
                uint pointerToRawData = br.ReadUInt32();
                fs.Seek(16, SeekOrigin.Current); // rest of section header

                long sectionEnd = pointerToRawData + sizeOfRawData;
                if (sectionEnd > maxEnd)
                    maxEnd = sectionEnd;
            }

            return maxEnd;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// The .NET single-file apphost contains a SHA-256 bundle signature followed by
    /// a 64-bit offset to the bundle header. When PE resources change, the PE size
    /// shifts, so we must find and update this offset.
    /// The signature is the bytes: 0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,
    ///                              0x72,0x7b,0x63,0x21,0xab,0x4b,0xc5,0x4b
    /// followed by the int64 offset.
    /// </summary>
    private static void PatchBundleOffset(string exePath, long oldPeEnd, long newPeEnd)
    {
        byte[] bundleSig = { 0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
                             0x72, 0x7b, 0x63, 0x21, 0xab, 0x4b, 0xc5, 0x4b };

        byte[] fileBytes = File.ReadAllBytes(exePath);
        long delta = newPeEnd - oldPeEnd;

        // Search for the bundle signature in the PE portion (before the appended data)
        int searchLimit = (int)Math.Min(newPeEnd, fileBytes.Length - bundleSig.Length - 8);
        for (int i = 0; i < searchLimit; i++)
        {
            bool match = true;
            for (int j = 0; j < bundleSig.Length; j++)
            {
                if (fileBytes[i + j] != bundleSig[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                int offsetPos = i + bundleSig.Length;
                long oldOffset = BitConverter.ToInt64(fileBytes, offsetPos);
                long newOffset = oldOffset + delta;
                BitConverter.GetBytes(newOffset).CopyTo(fileBytes, offsetPos);

                File.WriteAllBytes(exePath, fileBytes);
                Console.WriteLine($"  Patched .NET bundle offset: {oldOffset} -> {newOffset}");
                return;
            }
        }

        Console.Error.WriteLine("  Warning: Could not find .NET bundle signature to patch offset.");
    }

    private static T MemRead<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(byte[] data, int offset) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(data, offset, ptr, size);
            return Marshal.PtrToStructure<T>(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
