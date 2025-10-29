using System.Security.Cryptography;
using System.Text;
using NzbWebDAV.Models;
using SharpCompress.Common.Rar.Headers;

namespace NzbWebDAV.Extensions;

public static class RarHeaderExtensions
{
    public static byte GetCompressionMethod(this IRarHeader header)
    {
        return (byte)header.GetReflectionProperty("CompressionMethod")!;
    }

    public static long GetDataStartPosition(this IRarHeader header)
    {
        return (long)header.GetReflectionProperty("DataStartPosition")!;
    }

    public static long GetAdditionalDataSize(this IRarHeader header)
    {
        return (long)header.GetReflectionProperty("AdditionalDataSize")!;
    }

    public static long GetCompressedSize(this IRarHeader header)
    {
        return (long)header.GetReflectionProperty("CompressedSize")!;
    }

    public static long GetUncompressedSize(this IRarHeader header)
    {
        return (long)header.GetReflectionProperty("UncompressedSize")!;
    }

    public static string GetFileName(this IRarHeader header)
    {
        return (string)header.GetReflectionProperty("FileName")!;
    }

    public static bool IsDirectory(this IRarHeader header)
    {
        return (bool)header.GetReflectionProperty("IsDirectory")!;
    }

    public static int? GetVolumeNumber(this IRarHeader header)
    {
        return header.HeaderType == HeaderType.Archive
            ? (int?)header.GetReflectionProperty("VolumeNumber")
            : (short?)header.GetReflectionProperty("VolumeNumber");
    }

    public static bool GetIsFirstVolume(this IRarHeader header)
    {
        return (bool)header.GetReflectionProperty("IsFirstVolume")!;
    }

    public static byte[]? GetR4Salt(this IRarHeader header)
    {
        return (byte[]?)header.GetReflectionProperty("R4Salt")!;
    }

    public static object? GetRar5CryptoInfo(this IRarHeader header)
    {
        return header.GetReflectionProperty("Rar5CryptoInfo")!;
    }

    public static bool GetIsEncrypted(this IRarHeader header)
    {
        return (bool)header.GetReflectionProperty("IsEncrypted")!;
    }

    public static bool GetIsSolid(this IRarHeader header)
    {
        return (bool)header.GetReflectionProperty("IsSolid")!;
    }

    public static AesParams? GetAesParams(this IRarHeader header, string? password)
    {
        // sanity checks
        if (header.HeaderType != HeaderType.File) return null;
        if (password == null) return null;

        // rar3 aes params
        var r4Salt = header.GetR4Salt();
        if (r4Salt != null) return GetRar3AesParams(r4Salt, password, header.GetUncompressedSize());

        // rar5 aes params
        var rar5CryptoInfo = header.GetRar5CryptoInfo();
        if (rar5CryptoInfo != null) return GetRar5AesParams(rar5CryptoInfo, password, header.GetUncompressedSize());

        // no aes params
        return null;
    }

    private static AesParams? GetRar3AesParams(byte[] salt, string password, long decodedSize)
    {
        const int sizeInitV = 0x10;
        const int sizeSalt30 = 0x08;
        var aesIV = new byte[sizeInitV];

        var rawLength = 2 * password.Length;
        var rawPassword = new byte[rawLength + sizeSalt30];
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        for (var i = 0; i < password.Length; i++)
        {
            rawPassword[i * 2] = passwordBytes[i];
            rawPassword[(i * 2) + 1] = 0;
        }

        for (var i = 0; i < salt.Length; i++)
        {
            rawPassword[i + rawLength] = salt[i];
        }

        var msgDigest = SHA1.Create();
        const int noOfRounds = (1 << 18);
        const int iblock = 3;

        byte[] digest;
        var data = new byte[(rawPassword.Length + iblock) * noOfRounds];

        //TODO slow code below, find ways to optimize
        for (var i = 0; i < noOfRounds; i++)
        {
            rawPassword.CopyTo(data, i * (rawPassword.Length + iblock));

            data[(i * (rawPassword.Length + iblock)) + rawPassword.Length + 0] = (byte)i;
            data[(i * (rawPassword.Length + iblock)) + rawPassword.Length + 1] = (byte)(i >> 8);
            data[(i * (rawPassword.Length + iblock)) + rawPassword.Length + 2] = (byte)(i >> 16);

            if (i % (noOfRounds / sizeInitV) == 0)
            {
                digest = msgDigest.ComputeHash(data, 0, (i + 1) * (rawPassword.Length + iblock));
                aesIV[i / (noOfRounds / sizeInitV)] = digest[19];
            }
        }

        digest = msgDigest.ComputeHash(data);
        //slow code ends

        var aesKey = new byte[sizeInitV];
        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                aesKey[(i * 4) + j] = (byte)(
                    (
                        ((digest[i * 4] * 0x1000000) & 0xff000000)
                        | (uint)((digest[(i * 4) + 1] * 0x10000) & 0xff0000)
                        | (uint)((digest[(i * 4) + 2] * 0x100) & 0xff00)
                        | (uint)(digest[(i * 4) + 3] & 0xff)
                    ) >> (j * 8)
                );
            }
        }

        return new AesParams()
        {
            Iv = aesIV,
            Key = aesKey,
            DecodedSize = decodedSize,
        };
    }

    private static AesParams? GetRar5AesParams(object rar5CryptoInfo, string password, long decodedSize)
    {
        const int derivedKeyLength = 0x10;
        const int sizePswCheck = 0x08;
        const int sha256DigestSize = 32;
        var lg2Count = (int)rar5CryptoInfo.GetReflectionField("LG2Count")!;
        var salt = (byte[])rar5CryptoInfo.GetReflectionField("Salt")!;
        var usePswCheck = (bool)rar5CryptoInfo.GetReflectionField("UsePswCheck")!;
        var pswCheck = (byte[])rar5CryptoInfo.GetReflectionField("PswCheck")!;
        var initIv = (byte[])rar5CryptoInfo.GetReflectionField("InitV")!;

        var iterations = (1 << lg2Count); // Adjust the number of iterations as needed

        var salt_rar5 = salt.Concat(new byte[] { 0, 0, 0, 1 });
        var derivedKey = GenerateRarPbkdf2Key(
            password,
            salt_rar5.ToArray(),
            iterations,
            derivedKeyLength
        );

        var derivedPswCheck = new byte[sizePswCheck];
        for (var i = 0; i < sha256DigestSize; i++)
        {
            derivedPswCheck[i % sizePswCheck] ^= derivedKey[2][i];
        }

        if (usePswCheck && !pswCheck.SequenceEqual(derivedPswCheck))
        {
            throw new CryptographicException("The password did not match.");
        }

        return new AesParams()
        {
            Iv = initIv,
            Key = derivedKey[0],
            DecodedSize = decodedSize,
        };
    }


    private static List<byte[]> GenerateRarPbkdf2Key(
        string password,
        byte[] salt,
        int iterations,
        int keyLength
    )
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(password));
        var block = hmac.ComputeHash(salt);
        var finalHash = (byte[])block.Clone();

        var loop = new int[] { iterations, 17, 17 };
        var res = new List<byte[]> { };

        for (var x = 0; x < 3; x++)
        {
            for (var i = 1; i < loop[x]; i++)
            {
                block = hmac.ComputeHash(block);
                for (var j = 0; j < finalHash.Length; j++)
                {
                    finalHash[j] ^= block[j];
                }
            }

            res.Add((byte[])finalHash.Clone());
        }

        return res;
    }
}