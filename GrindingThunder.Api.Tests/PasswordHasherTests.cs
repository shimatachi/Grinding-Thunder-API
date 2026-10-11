using GrindingThunder.Api.Infrastructure.Security;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void HashPassword_ValidPassword_ReturnsSaltAndHashString()
    {
        var password = "SecurePassword123!";
        var hash = _hasher.HashPassword(password);

        Assert.NotNull(hash);
        var parts = hash.Split('.');
        Assert.Equal(2, parts.Length);

        var saltBytes = Convert.FromBase64String(parts[0]);
        var hashBytes = Convert.FromBase64String(parts[1]);

        Assert.Equal(16, saltBytes.Length);
        Assert.Equal(32, hashBytes.Length);
    }

    [Fact]
    public void HashPassword_SamePasswordProducesDifferentHashesDueToSalt()
    {
        var password = "SamePassword!";
        var hash1 = _hasher.HashPassword(password);
        var hash2 = _hasher.HashPassword(password);

        Assert.NotEqual(hash1, hash2);
        Assert.True(_hasher.VerifyPassword(password, hash1));
        Assert.True(_hasher.VerifyPassword(password, hash2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void HashPassword_EmptyOrWhitespace_ThrowsArgumentException(string invalidPassword)
    {
        Assert.ThrowsAny<ArgumentException>(() => _hasher.HashPassword(invalidPassword));
    }

    [Fact]
    public void VerifyPassword_MatchingPassword_ReturnsTrue()
    {
        var password = "AdminPassword456$";
        var hash = _hasher.HashPassword(password);

        Assert.True(_hasher.VerifyPassword(password, hash));
    }

    [Fact]
    public void VerifyPassword_IncorrectPassword_ReturnsFalse()
    {
        var password = "AdminPassword456$";
        var hash = _hasher.HashPassword(password);

        Assert.False(_hasher.VerifyPassword("WrongPassword!", hash));
    }

    [Theory]
    [InlineData("", "somehash")]
    [InlineData("password", "")]
    [InlineData("password", "singlepartstring")]
    [InlineData("password", "part1.part2.part3")]
    [InlineData("password", "invalidbase64!.invalidbase64!")]
    public void VerifyPassword_InvalidInputsOrFormat_ReturnsFalse(string password, string hash)
    {
        Assert.False(_hasher.VerifyPassword(password, hash));
    }
}

