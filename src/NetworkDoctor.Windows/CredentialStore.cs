namespace NetworkDoctor.Windows;

using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

public interface ICredentialStore
{
    void Save(string password);

    string? Load();
}

public sealed class DpapiCredentialStore : ICredentialStore
{
    private const int CryptProtectUiForbidden = 0x1;
    private readonly string _path;

    public DpapiCredentialStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetworkDoctorRouterCred.xml");
    }

    public void Save(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var protectedBytes = Protect(Encoding.UTF8.GetBytes(password));
        File.WriteAllText(_path, Convert.ToBase64String(protectedBytes));
    }

    public string? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var encoded = File.ReadAllText(_path).Trim();
            var unprotectedBytes = Unprotect(Convert.FromBase64String(encoded));
            return Encoding.UTF8.GetString(unprotectedBytes);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            FormatException or
            CryptographicException or
            InvalidOperationException)
        {
            return null;
        }
    }

    private static byte[] Protect(byte[] plain)
    {
        var input = ToBlob(plain);
        if (!CryptProtectData(
                ref input,
                "NetworkDoctor",
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                CryptProtectUiForbidden,
                out var output))
        {
            throw new InvalidOperationException($"CryptProtectData failed: {Marshal.GetLastWin32Error()}");
        }

        try
        {
            return Copy(output);
        }
        finally
        {
            FreeBlobs(input, output);
        }
    }

    private static byte[] Unprotect(byte[] cipher)
    {
        var input = ToBlob(cipher);
        if (!CryptUnprotectData(
                ref input,
                null,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                CryptProtectUiForbidden,
                out var output))
        {
            throw new InvalidOperationException($"CryptUnprotectData failed: {Marshal.GetLastWin32Error()}");
        }

        try
        {
            return Copy(output);
        }
        finally
        {
            FreeBlobs(input, output);
        }
    }

    private static DATA_BLOB ToBlob(byte[] data)
    {
        var pointer = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, pointer, data.Length);
        return new DATA_BLOB { Size = data.Length, Pointer = pointer };
    }

    private static byte[] Copy(DATA_BLOB blob)
    {
        var result = new byte[blob.Size];
        Marshal.Copy(blob.Pointer, result, 0, blob.Size);
        return result;
    }

    private static void FreeBlobs(DATA_BLOB input, DATA_BLOB output)
    {
        if (output.Pointer != IntPtr.Zero)
        {
            LocalFree(output.Pointer);
        }

        if (input.Pointer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(input.Pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int Size;
        public IntPtr Pointer;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB input,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DATA_BLOB output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB input,
        StringBuilder? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DATA_BLOB output);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
