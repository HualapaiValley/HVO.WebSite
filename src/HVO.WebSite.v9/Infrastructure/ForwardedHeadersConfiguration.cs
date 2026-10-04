using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace HVO.WebSite.v9.Infrastructure;

internal static class ForwardedHeadersConfiguration
{
    public static void Configure(IServiceCollection services)
    {
        // Resolve final host configuration after all application/host providers.
        // ValidateOnStart also catches malformed trust configuration while disabled.
        services.AddOptions<ForwardedHeadersOptions>().PostConfigure<IConfiguration>((options, configuration) =>
        {
            // Parse at startup, including disabled configurations, so a later rollout
            // cannot silently activate malformed or unrestricted proxy trust.
            var proxySection = configuration.GetSection("ForwardedHeaders:KnownProxies");
            var networkSection = configuration.GetSection("ForwardedHeaders:KnownNetworks");
            var proxies = proxySection.Get<string[]>() ?? [];
            var networks = networkSection.Get<string[]>() ?? [];
            var parsedProxies = proxies.Select(value => IPAddress.TryParse(value, out var address)
                && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any)
                ? address : throw new InvalidOperationException($"Invalid ForwardedHeaders:KnownProxies entry '{value}'.")).ToArray();
            var parsedNetworks = networks.Select(value => System.Net.IPNetwork.TryParse(value, out var network) && network.PrefixLength > 0
                ? network : throw new InvalidOperationException($"Invalid ForwardedHeaders:KnownNetworks entry '{value}'.")).ToArray();
            if ((proxySection.Exists() || networkSection.Exists()) && parsedProxies.Length + parsedNetworks.Length == 0)
                throw new InvalidOperationException("ForwardedHeaders requires at least one known proxy or network when trust lists are configured.");

            // Override the framework host switch's initial unrestricted trust lists.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
#pragma warning disable ASPDEPR005
            options.KnownNetworks.Clear();
#pragma warning restore ASPDEPR005
            if (parsedProxies.Length + parsedNetworks.Length == 0)
            {
                options.KnownProxies.Add(IPAddress.IPv6Loopback);
                options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Loopback, 8));
            }
            else
            {
                foreach (var proxy in parsedProxies) options.KnownProxies.Add(proxy);
                foreach (var network in parsedNetworks) options.KnownIPNetworks.Add(network);
            }
        }).ValidateOnStart();
    }
}
