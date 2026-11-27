using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Yoji.U3DAILinker.Operations
{
    /// macOS/Linux implementation: directory links are symlinks created through libc.
    /// Unity's .NET Standard 2.0 profile has no Directory.CreateSymbolicLink or LinkTarget, hence P/Invoke.
    /// Selected by JunctionManagers.ForCurrentPlatform() off Windows only.
    internal sealed class UnixSymlinkManager : IJunctionManager
    {
        [DllImport("libc", SetLastError = true)]
        private static extern int symlink(string target, string linkPath);

        [DllImport("libc", SetLastError = true)]
        private static extern IntPtr readlink(string path, byte[] buffer, IntPtr bufferSize);

        [DllImport("libc", SetLastError = true)]
        private static extern int unlink(string path);

        public bool IsJunction(string linkPath)
        {
            // lstat-based: also true for a dangling link, which Directory.Exists reports as missing.
            try { return (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        public string GetTarget(string linkPath)
        {
            if (!IsJunction(linkPath)) return null;
            for (var size = 256; ; size *= 2)
            {
                var buffer = new byte[size];
                var length = readlink(linkPath, buffer, new IntPtr(size)).ToInt64();
                if (length < 0) return null;
                // readlink does not NUL-terminate and silently truncates when the buffer is full.
                if (length < size) return Encoding.UTF8.GetString(buffer, 0, (int)length);
            }
        }

        public void Create(string linkPath, string targetDir)
        {
            if (linkPath == null) throw new ArgumentNullException(nameof(linkPath));
            if (!Directory.Exists(targetDir)) throw new DirectoryNotFoundException("symlink target missing: " + targetDir);
            // Absolute target: callers compare GetTarget() with full paths, and a relative one would resolve against cwd.
            if (symlink(Path.GetFullPath(targetDir), linkPath) != 0)
                throw LastError("symlink", linkPath);
        }

        public void Delete(string linkPath)
        {
            if (!IsJunction(linkPath)) return; // never touch a real directory
            if (unlink(linkPath) != 0)
                throw LastError("unlink", linkPath);
        }

        private static IOException LastError(string call, string path)
            => new IOException(call + " failed for " + path + ": errno " + Marshal.GetLastWin32Error());
    }
}
