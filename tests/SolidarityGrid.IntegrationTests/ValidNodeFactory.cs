using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace SolidarityGrid.IntegrationTests;

public sealed class ValidNodeFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Node:NodeId"] = "test-node",
                ["Node:PublicUrl"] = "http://localhost:5999",
                ["Node:InternalUrl"] = "http://test-node:8080",
                ["Node:Environment"] = "IntegrationTest",
                ["Node:Peers:0:NodeId"] = "peer-one",
                ["Node:Peers:0:Url"] = "http://peer-one:8080",
                ["Node:Peers:1:NodeId"] = "peer-two",
                ["Node:Peers:1:Url"] = "http://peer-two:8080",
            });
        });
    }
}
