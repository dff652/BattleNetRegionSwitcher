using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BattleNetRegionSwitcher.Core;

/// <summary>Resolves the directory seen by this process to a path Explorer can open.</summary>
[SupportedOSPlatform("windows")]
public static class ShellDirectory
{
    public static string ResolvePath(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        if ((File.GetAttributes(fullPath) & FileAttributes.Directory) == 0)
            throw new IOException("The target must be a directory.");

        // Packaged launch environments can redirect AppData without changing the
        // logical path. Resolve the opened handle instead of guessing a package name.
        const uint openExisting = 3;
        const uint backupSemantics = 0x02000000;
        using var handle = CreateFileW(fullPath, 0, FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero, openExisting, backupSemantics, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());

        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (length >= buffer.Capacity) throw new PathTooLongException();

        var physicalPath = buffer.ToString();
        // Explorer expects ordinary drive/UNC paths, rather than device-path syntax.
        if (physicalPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + physicalPath[8..];
        if (physicalPath.StartsWith(@"\\?\", StringComparison.Ordinal) &&
            physicalPath.Length >= 7 && physicalPath[5] == ':' && physicalPath[6] == '\\')
            return physicalPath[4..];
        return physicalPath;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess,
        FileShare shareMode, IntPtr securityAttributes, uint creationDisposition,
        uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file,
        StringBuilder filePath, uint filePathSize, uint flags);
}
