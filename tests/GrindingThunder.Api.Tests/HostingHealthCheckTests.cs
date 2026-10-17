using System.Net;
using Xunit;

namespace GrindingThunder.Api.Tests;

public class HostingHealthCheckTests
{
    [Fact]
    public async Task Health_IsAnonymousAndHealthy()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
