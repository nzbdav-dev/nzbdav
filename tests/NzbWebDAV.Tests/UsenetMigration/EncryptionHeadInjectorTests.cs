using NzbWebDAV.UsenetMigration.Model;
using NzbWebDAV.UsenetMigration.Nzb;

namespace NzbWebDAV.Tests.UsenetMigration;

public class EncryptionHeadInjectorTests
{
    private const string BareNzb =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<nzb xmlns=\"http://www.newzbin.com/DTD/2003/nzb\">\n" +
        "  <file poster=\"p\" date=\"1\" subject=\"s\">\n  </file>\n</nzb>\n";

    [Fact]
    public void NoEncryptionNoPassword_ReturnsUnchanged()
    {
        var meta = new AltmountFileMetadata { Encryption = AltmountEncryption.None, Password = "" };
        Assert.Equal(BareNzb, EncryptionHeadInjector.Inject(BareNzb, meta));
    }

    [Fact]
    public void RcloneWithPasswordAndSalt_InjectsHeadAfterNzbTag()
    {
        var meta = new AltmountFileMetadata
        {
            Encryption = AltmountEncryption.Rclone,
            Password = "pw",
            Salt = "st",
        };
        var result = EncryptionHeadInjector.Inject(BareNzb, meta);

        Assert.Contains("<nzb xmlns=\"http://www.newzbin.com/DTD/2003/nzb\">\n  <head>\n", result);
        Assert.Contains("    <meta type=\"cipher\">rclone</meta>\n", result);
        Assert.Contains("    <meta type=\"password\">pw</meta>\n", result);
        Assert.Contains("    <meta type=\"salt\">st</meta>\n", result);
        Assert.Contains("  </head>\n", result);
    }

    [Fact]
    public void PasswordWithDollar_SurvivesLiterally()
    {
        // The regex replacement must be literal — a "$" in the password must not
        // be interpreted as a substitution group.
        var meta = new AltmountFileMetadata { Encryption = AltmountEncryption.None, Password = "a$1b" };
        var result = EncryptionHeadInjector.Inject(BareNzb, meta);
        Assert.Contains("<meta type=\"password\">a$1b</meta>", result);
    }

    [Fact]
    public void PasswordOnly_NoEncryption_OmitsCipherMeta()
    {
        var meta = new AltmountFileMetadata { Encryption = AltmountEncryption.None, Password = "pw" };
        var result = EncryptionHeadInjector.Inject(BareNzb, meta);
        Assert.DoesNotContain("type=\"cipher\"", result);
        Assert.Contains("<meta type=\"password\">pw</meta>", result);
    }

    [Fact]
    public void AesEncryption_MapsToNone_PreservingUpstreamQuirk()
    {
        // Altmount's convertEncryptionToString falls AES through to "none".
        Assert.Equal("none", EncryptionHeadInjector.EncryptionToString(AltmountEncryption.Aes));
        Assert.Equal("rclone", EncryptionHeadInjector.EncryptionToString(AltmountEncryption.Rclone));
        Assert.Equal("headers", EncryptionHeadInjector.EncryptionToString(AltmountEncryption.Headers));
    }
}
