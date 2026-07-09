using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using PropertyIntelligence.Providers.Ana;
using PropertyIntelligence.Core.Domain;

namespace PropertyIntelligence.Tests.Contract
{
    public class PublicDataProvidersTests
    {
        [Fact]
        public async Task AnaFloodRiskProvider_WithIntersectingZones_ReturnsHighestRiskAndRawPayload()
        {
            // executor returns JSON array of zones
            Func<double, double, System.Threading.CancellationToken, Task<string>> executor = (lat, lng, ct) =>
            {
                var json = "["
                    + "{ \"severity\": \"low\", \"distance_metres\": 1200 },"
                    + "{ \"severity\": \"critical\", \"distance_metres\": 0 }"
                    + "]";
                return Task.FromResult(json);
            };

            var provider = new AnaFloodRiskProvider(executor);
            var address = new PropertyAddress { Lat = -23.55, Lng = -46.63 };

            var result = await provider.FetchAsync(address);

            Assert.NotNull(result.RawPayload);
            Assert.Equal("critical", result.Data.RiskLevel);
            Assert.Equal(0, result.Data.DistanceMetres);
            Assert.Equal(TrendDirection.Stable, result.Data.Trend);
        }

        [Fact]
        public async Task AnaFloodRiskProvider_WhenNoZoneIntersects_ReturnsNullRisk()
        {
            Func<double, double, System.Threading.CancellationToken, Task<string>> executor = (lat, lng, ct) =>
            {
                var json = "[]";
                return Task.FromResult(json);
            };

            var provider = new AnaFloodRiskProvider(executor);
            var address = new PropertyAddress { Lat = -23.55, Lng = -46.63 };

            var result = await provider.FetchAsync(address);

            Assert.NotNull(result.RawPayload);
            Assert.Null(result.Data.RiskLevel);
            Assert.Null(result.Data.DistanceMetres);
        }

        [Fact]
        public async Task AnaFloodRiskProvider_WithoutCoordinates_ThrowsBeforeQuery()
        {
            bool called = false;
            Func<double, double, System.Threading.CancellationToken, Task<string>> executor = (lat, lng, ct) =>
            {
                called = true;
                return Task.FromResult("[]");
            };

            var provider = new AnaFloodRiskProvider(executor);
            var address = new PropertyAddress { Lat = null, Lng = null };

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.FetchAsync(address));
            Assert.False(called, "Executor should not have been invoked when coordinates are missing");
        }
    }
}
