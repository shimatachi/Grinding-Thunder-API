using System.Text.RegularExpressions;
using GrindingThunder.Admin.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace GrindingThunder.Admin.Tests;

public class AdminWebApplicationFactory : WebApplicationFactory<Program>
{
    public TestMockApiHandler MockApiHandler { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
            {
                options.HttpMessageHandlerBuilderActions.Add(b =>
                {
                    b.PrimaryHandler = MockApiHandler;
                });
            });
        });
    }

    public static async Task<string> ExtractAntiforgeryTokenAsync(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, @"name=""__RequestVerificationToken""\s+(?:type=""hidden""\s+)?value=""([^""]+)""");
        if (!match.Success)
        {
            match = Regex.Match(html, @"value=""([^""]+)""\s+name=""__RequestVerificationToken""");
        }
        return match.Groups[1].Value;
    }
}

