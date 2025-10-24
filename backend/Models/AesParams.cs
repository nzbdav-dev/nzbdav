using System.Security.Cryptography;
using System.Text;

namespace NzbWebDAV.Models;

public class AesParams()
{
    public long DecodedSize { get; set; } = 0;
    public byte[] Iv { get; set; } = [];
    public byte[] Key { get; set; } = [];

    public static AesParams? FromCoderInfo(byte[]? coderInfo, string? password, long decodedSize)
    {
        if (coderInfo == null || password == null) return null;
        return new AesParams(coderInfo, password, decodedSize);
    }

    private AesParams(byte[] info, string password, long decodedSize) : this()
    {
        Init(info, out var numCyclesPower, out var salt, out var iv);
        Key = InitKey(numCyclesPower, salt, Encoding.Unicode.GetBytes(password));
        DecodedSize = decodedSize;
        Iv = iv;
    }

    private static void Init(byte[] info, out int numCyclesPower, out byte[] salt, out byte[] iv)
    {
        var bt = info[0];
        numCyclesPower = bt & 0x3F;

        if ((bt & 0xC0) == 0)
        {
            salt = [];
            iv = [];
            return;
        }

        var saltSize = (bt >> 7) & 1;
        var ivSize = (bt >> 6) & 1;
        if (info.Length == 1)
            throw new InvalidOperationException();

        var bt2 = info[1];
        saltSize += (bt2 >> 4);
        ivSize += (bt2 & 15);
        if (info.Length < 2 + saltSize + ivSize)
            throw new InvalidOperationException();

        salt = new byte[saltSize];
        for (var i = 0; i < saltSize; i++)
            salt[i] = info[i + 2];

        iv = new byte[16];
        for (var i = 0; i < ivSize; i++)
            iv[i] = info[i + saltSize + 2];

        if (numCyclesPower > 24)
            throw new NotSupportedException();
    }

    private byte[] InitKey(int mNumCyclesPower, byte[] salt, byte[] pass)
    {
        if (mNumCyclesPower == 0x3F)
        {
            var key = new byte[32];
            int pos;
            for (pos = 0; pos < salt.Length; pos++)
                key[pos] = salt[pos];

            for (var i = 0; i < pass.Length && pos < 32; i++)
                key[pos++] = pass[i];

            return key;
        }

        using var sha = SHA256.Create();
        var counter = new byte[8];
        var numRounds = 1L << mNumCyclesPower;
        for (long round = 0; round < numRounds; round++)
        {
            sha.TransformBlock(salt, 0, salt.Length, null, 0);
            sha.TransformBlock(pass, 0, pass.Length, null, 0);
            sha.TransformBlock(counter, 0, 8, null, 0);

            for (var i = 0; i < 8; i++)
            {
                if (++counter[i] != 0)
                    break;
            }
        }

        sha.TransformFinalBlock(counter, 0, 0);
        var shaKey = sha.Hash;
        if (shaKey == null) throw new InvalidOperationException("Initialized AES with null key");
        return shaKey;
    }
}