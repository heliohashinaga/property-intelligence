using FluentAssertions;
using PropertyIntelligence.Core.Registry;
using PropertyIntelligence.Providers.Registry;

namespace PropertyIntelligence.Tests.Unit;

/// <summary>
/// T086 DoD: the provider registry drives selection so that disabled providers
/// are skipped BEFORE fan-out, and the enrichment module resolves enabled
/// providers from the registry.
/// </summary>
public class ProviderRegistryTests
{
    private static ProviderDescriptor Mock(string id, bool enabled) => new()
    {
        ProviderId = id,
        DisplayName = id,
        Enabled = enabled,
        Capabilities = new[] { id.Replace("mock_", "") },
        CacheTtl = TimeSpan.FromSeconds(2592000),
        Timeout = TimeSpan.FromSeconds(5),
        SourceType = SourceType.Imported,
        Version = "1.0.0",
    };

    [Fact]
    public void GetEnabled_skips_disabled_providers()
    {
        var descriptors = new[]
        {
            Mock("mock_address",       enabled: true),
            Mock("mock_mobility",      enabled: true),
            Mock("mock_security",      enabled: false), // disabled
            Mock("mock_environment",   enabled: true),
        };

        var registry = new ProviderRegistry(descriptors);

        var enabledIds = registry.GetEnabled().Select(d => d.ProviderId).ToList();

        enabledIds.Should().BeEquivalentTo(new[]
        {
            "mock_address", "mock_mobility", "mock_environment"
        });
        enabledIds.Should().NotContain("mock_security");
    }

    [Fact]
    public void Get_returns_descriptor_regardless_of_enabled_state()
    {
        var registry = new ProviderRegistry(new[]
        {
            Mock("mock_security", enabled: false),
            Mock("mock_address",  enabled: true),
        });

        registry.Get("mock_security").Should().NotBeNull();
        registry.Get("mock_address").Should().NotBeNull();
        registry.Get("unknown").Should().BeNull();
    }

}
