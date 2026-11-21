using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;

namespace CtrlZed;

internal static class CzdProtection
{
    // A versioned envelope: magic, salt and nonce (authenticated header),
    // followed by the authentication tag and encrypted .czd archive.
    static ReadOnlySpan<byte> Magic => "CZDPW001"u8;
    const int MagicSize = 8;
    const int SaltSize = 16;
    const int NonceSize = 12;
    const int HeaderSize = MagicSize + SaltSize + NonceSize;
    const int TagSize = 16;
    const int Iterations = 600_000;

    public static bool IsProtected(Stream source)
    {
        var originalPosition = source.Position;
        try
        {
            Span<byte> marker = stackalloc byte[MagicSize];
            return source.ReadAtLeast(marker, marker.Length, throwOnEndOfStream: false) == marker.Length
                && marker.SequenceEqual(Magic);
        }
        finally
        {
            source.Position = originalPosition;
        }
    }

    public static void Encrypt(Stream destination, ReadOnlySpan<byte> archive, SecureString password)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        Magic.CopyTo(header);
        var salt = header.Slice(MagicSize, SaltSize);
        var nonce = header.Slice(MagicSize + SaltSize, NonceSize);
        RandomNumberGenerator.Fill(salt);
        RandomNumberGenerator.Fill(nonce);

        Span<byte> key = stackalloc byte[32];
        try
        {
            DeriveKey(password, salt, key);
            using var aes = new AesGcm(key, TagSize);
            Span<byte> tag = stackalloc byte[TagSize];
            var ciphertext = new byte[archive.Length];
            aes.Encrypt(nonce, archive, ciphertext, tag, header);
            destination.Write(header);
            destination.Write(tag);
            destination.Write(ciphertext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static byte[] Decrypt(Stream source, SecureString password)
    {
        var archiveLength = source.Length - source.Position - HeaderSize - TagSize;
        if (archiveLength <= 0 || archiveLength > Array.MaxLength)
            throw new InvalidDataException("The protected CtrlZed file is incomplete or is too large to open.");

        Span<byte> header = stackalloc byte[HeaderSize];
        source.ReadExactly(header);
        if (!header[..MagicSize].SequenceEqual(Magic))
            throw new InvalidDataException("This is not a supported protected CtrlZed document.");
        var salt = header.Slice(MagicSize, SaltSize);
        var nonce = header.Slice(MagicSize + SaltSize, NonceSize);
        Span<byte> tag = stackalloc byte[TagSize];
        source.ReadExactly(tag);
        var ciphertext = new byte[(int)archiveLength];
        source.ReadExactly(ciphertext);

        Span<byte> key = stackalloc byte[32];
        var archive = new byte[ciphertext.Length];
        try
        {
            DeriveKey(password, salt, key);
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, archive, header);
            return archive;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(archive);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    static void DeriveKey(SecureString password, ReadOnlySpan<byte> salt, Span<byte> key)
    {
        var characters = new char[password.Length];
        var pointer = Marshal.SecureStringToGlobalAllocUnicode(password);
        try
        {
            Marshal.Copy(pointer, characters, 0, characters.Length);
            Rfc2898DeriveBytes.Pbkdf2(characters, salt, key, Iterations, HashAlgorithmName.SHA256);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(characters.AsSpan()));
            Marshal.ZeroFreeGlobalAllocUnicode(pointer);
        }
    }

    public static bool PasswordsMatch(SecureString password, SecureString confirmation)
    {
        if (password.Length != confirmation.Length) return false;
        var first = Marshal.SecureStringToGlobalAllocUnicode(password);
        var second = IntPtr.Zero;
        try
        {
            second = Marshal.SecureStringToGlobalAllocUnicode(confirmation);
            var difference = 0;
            for (var i = 0; i < password.Length; i++)
                difference |= Marshal.ReadInt16(first, i * sizeof(char)) ^ Marshal.ReadInt16(second, i * sizeof(char));
            return difference == 0;
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(first);
            if (second != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(second);
        }
    }
}

