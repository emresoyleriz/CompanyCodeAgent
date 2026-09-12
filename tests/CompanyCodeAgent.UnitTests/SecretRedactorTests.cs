using CompanyCodeAgent.Domain;

namespace CompanyCodeAgent.UnitTests;

public sealed class SecretRedactorTests
{
    [Theory]
    [InlineData("api_key=super-secret", "api_key=[REDACTED]")]
    [InlineData("Password: pass123", "Password=[REDACTED]")]
    [InlineData("ConnectionString=Server=db;Password=secret", "ConnectionString=[REDACTED];Password=[REDACTED]")]
    public void Redacts_Sensitive_Values(string input, string expected)
    {
        Assert.Equal(expected, SecretRedactor.Redact(input));
    }
}
