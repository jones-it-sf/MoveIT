using MoveIT.Core.Util;

namespace MoveIT.Core.Tests;

public class PasswordGeneratorTests
{
    [Theory]
    [InlineData(12)]
    [InlineData(20)]
    [InlineData(64)]
    public void MeetsComplexityAndLength(int length)
    {
        for (int i = 0; i < 200; i++)
        {
            var password = PasswordGenerator.Generate(length);

            Assert.Equal(length, password.Length);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsDigit);
            Assert.Contains(password, c => !char.IsLetterOrDigit(c));
            Assert.DoesNotContain(password, c => "Il1O0".Contains(c));
        }
    }

    [Fact]
    public void ProducesDifferentPasswords()
    {
        var passwords = Enumerable.Range(0, 100).Select(_ => PasswordGenerator.Generate(20)).ToHashSet();

        Assert.Equal(100, passwords.Count);
    }

    [Fact]
    public void RejectsShortLengths()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordGenerator.Generate(8));
    }
}
