using System.Threading;
using System.Threading.Tasks;
using PropertyIntelligence.Core.Domain;

namespace PropertyIntelligence.Providers.ViaCep
{
    public interface IAddressCoordinateResolver
    {
        /// <summary>
        /// Attempts to resolve coordinates for the supplied address.
        /// Returns a (lat, lng) tuple when resolution succeeded, or (null, null) when not found.
        /// </summary>
        Task<(double? Lat, double? Lng)> ResolveAsync(PropertyAddress address, CancellationToken ct = default);
    }
}
