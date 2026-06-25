using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Yoji.U3DAILinker.Operations
{
    /// Windows directory junction（mount-point reparse point）的生产实现。
    /// 用 P/Invoke DeviceIoControl 直接写 reparse 数据，而非 spawn mklink 子进程
    /// （cross-project 经验：shell/子进程在不同环境不可靠，且无法稳定拿退出语义）。
    /// 仅在 Windows + 真实 Editor 下使用；EditMode 批跑用 FakeJunctionManager，不触碰真实 FS。
    internal sealed class WindowsJunctionManager : IJunctionManager
    {
        private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
        private const uint FSCTL_SET_REPARSE_POINT = 0x000900A4;
        private const uint FSCTL_GET_REPARSE_POINT = 0x000900A8;
        private const uint FSCTL_DELETE_REPARSE_POINT = 0x000900AC;

        private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_SHARE_READ = 0x1, FILE_SHARE_WRITE = 0x2, FILE_SHARE_DELETE = 0x4;
        private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        private const int FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const int MAX_REPARSE_SIZE = 16 * 1024;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode,
            byte[] lpInBuffer, int nInBufferSize, byte[] lpOutBuffer, int nOutBufferSize,
            out int lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetFileAttributes(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveDirectory(string lpPathName);

        public bool IsJunction(string linkPath)
        {
            if (!Directory.Exists(linkPath)) return false;
            var attr = GetFileAttributes(linkPath);
            if (attr == -1) return false;
            return (attr & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
        }

        public string GetTarget(string linkPath)
        {
            if (!IsJunction(linkPath)) return null;
            var handle = OpenReparse(linkPath, GENERIC_READ);
            try
            {
                var outBuf = new byte[MAX_REPARSE_SIZE];
                if (!DeviceIoControl(handle, FSCTL_GET_REPARSE_POINT, null, 0, outBuf, outBuf.Length, out _, IntPtr.Zero))
                    throw new IOException("FSCTL_GET_REPARSE_POINT failed: " + Marshal.GetLastWin32Error());
                return ParseMountPointTarget(outBuf);
            }
            finally { CloseHandle(handle); }
        }

        public void Create(string linkPath, string targetDir)
        {
            if (linkPath == null) throw new ArgumentNullException(nameof(linkPath));
            if (!Directory.Exists(targetDir)) throw new DirectoryNotFoundException("junction target missing: " + targetDir);

            Directory.CreateDirectory(linkPath); // junction 必须建在一个空目录上
            var handle = OpenReparse(linkPath, GENERIC_WRITE);
            try
            {
                var buffer = BuildMountPointBuffer(Path.GetFullPath(targetDir));
                if (!DeviceIoControl(handle, FSCTL_SET_REPARSE_POINT, buffer, buffer.Length, null, 0, out _, IntPtr.Zero))
                {
                    var err = Marshal.GetLastWin32Error();
                    CloseHandle(handle);
                    try { Directory.Delete(linkPath); } catch { }
                    throw new IOException("FSCTL_SET_REPARSE_POINT failed: " + err);
                }
            }
            finally { CloseHandle(handle); }
        }

        public void Delete(string linkPath)
        {
            if (!IsJunction(linkPath)) return; // 只删 junction，绝不误删普通目录
            var handle = OpenReparse(linkPath, GENERIC_WRITE);
            try
            {
                // 删 reparse 数据需要一个最小 header（仅 ReparseTag + 0 长度）
                var header = new byte[8];
                BitConverter.GetBytes(IO_REPARSE_TAG_MOUNT_POINT).CopyTo(header, 0);
                DeviceIoControl(handle, FSCTL_DELETE_REPARSE_POINT, header, header.Length, null, 0, out _, IntPtr.Zero);
            }
            finally { CloseHandle(handle); }
            RemoveDirectory(linkPath); // 此时 linkPath 是空目录壳，移除它
        }

        private static IntPtr OpenReparse(string path, uint access)
        {
            var handle = CreateFile(path, access,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
            if (handle == INVALID_HANDLE_VALUE)
                throw new IOException("CreateFile(reparse) failed for " + path + ": " + Marshal.GetLastWin32Error());
            return handle;
        }

        // REPARSE_DATA_BUFFER(mount point): ReparseTag(4) ReparseDataLength(2) Reserved(2)
        // SubstituteNameOffset(2) SubstituteNameLength(2) PrintNameOffset(2) PrintNameLength(2) PathBuffer(...)
        private static byte[] BuildMountPointBuffer(string targetFullPath)
        {
            var substitute = "\\??\\" + targetFullPath;
            var subBytes = Encoding.Unicode.GetBytes(substitute);
            var printBytes = Encoding.Unicode.GetBytes(targetFullPath);

            int subLen = subBytes.Length;
            int printLen = printBytes.Length;
            // PathBuffer = substitute + null(2) + print + null(2)
            int pathBufferLen = subLen + 2 + printLen + 2;
            int reparseDataLength = 8 + pathBufferLen; // 4 个 offset/length 字段(8) + path buffer
            var buffer = new byte[8 + reparseDataLength]; // 头 8 字节(tag+len+reserved) + 数据

            int p = 0;
            BitConverter.GetBytes(IO_REPARSE_TAG_MOUNT_POINT).CopyTo(buffer, p); p += 4;
            BitConverter.GetBytes((ushort)reparseDataLength).CopyTo(buffer, p); p += 2;
            BitConverter.GetBytes((ushort)0).CopyTo(buffer, p); p += 2; // Reserved

            ushort subOffset = 0;
            ushort subNameLen = (ushort)subLen;
            ushort printOffset = (ushort)(subLen + 2);
            ushort printNameLen = (ushort)printLen;
            BitConverter.GetBytes(subOffset).CopyTo(buffer, p); p += 2;
            BitConverter.GetBytes(subNameLen).CopyTo(buffer, p); p += 2;
            BitConverter.GetBytes(printOffset).CopyTo(buffer, p); p += 2;
            BitConverter.GetBytes(printNameLen).CopyTo(buffer, p); p += 2;

            Buffer.BlockCopy(subBytes, 0, buffer, p, subLen); p += subLen;
            p += 2; // null terminator (already zero)
            Buffer.BlockCopy(printBytes, 0, buffer, p, printLen);
            // 末尾 null 已是 0
            return buffer;
        }

        private static string ParseMountPointTarget(byte[] outBuf)
        {
            uint tag = BitConverter.ToUInt32(outBuf, 0);
            if (tag != IO_REPARSE_TAG_MOUNT_POINT) return null;
            // 头 8 字节后是 mount-point 专属字段
            ushort subOffset = BitConverter.ToUInt16(outBuf, 8);
            ushort subLen = BitConverter.ToUInt16(outBuf, 10);
            int pathStart = 8 + 8 + subOffset; // 8 头 + 8 字段 + offset
            var substitute = Encoding.Unicode.GetString(outBuf, pathStart, subLen);
            if (substitute.StartsWith("\\??\\")) substitute = substitute.Substring(4);
            return substitute;
        }
    }
}
