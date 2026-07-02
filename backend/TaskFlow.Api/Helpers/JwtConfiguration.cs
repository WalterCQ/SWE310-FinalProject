using System.Text;
using Microsoft.Extensions.Hosting;

namespace TaskFlow.Api.Helpers;

public static class JwtConfiguration
{
    public const string DefaultSigningKey = "superSecretKeyOfAtLeast32CharactersLong!!";

    public static string GetSigningKey(IConfiguration configuration, IHostEnvironment environment)
    {
        var signingKey = configuration["Jwt:SigningKey"];
        if (!string.IsNullOrWhiteSpace(signingKey))
        {
            return signingKey;
        }

        if (environment.IsDevelopment())
        {
            return DefaultSigningKey;
        }

        throw new InvalidOperationException("Jwt:SigningKey must be configured outside Development.");
    }

    public static byte[] GetSigningKeyBytes(IConfiguration configuration, IHostEnvironment environment)
    {
        return Encoding.UTF8.GetBytes(GetSigningKey(configuration, environment));
    }
}
