using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PropertyIntelligence.Core.Domain;

namespace PropertyIntelligence.Providers.ViaCep
{
    /// <summary>
    /// Simple in-memory CEP->coordinates resolver for local-first behavior in tests/MVP.
    /// </summary>
    public sealed class LocalCepCoordinateResolver : IAddressCoordinateResolver
    {
        private readonly Dictionary<string, (double Lat, double Lng)> _map;

        public LocalCepCoordinateResolver(Dictionary<string, (double Lat, double Lng)> map)
        {
            _map = map ?? new Dictionary<string, (double, double)>();
        }

        public Task<(double? Lat, double? Lng)> ResolveAsync(PropertyAddress address, CancellationToken ct = default)
        {
            if (address == null || string.IsNullOrWhiteSpace(address.PostalCode))
                return Task.FromResult<(double?, double?)>((null, null));

            var cep = System.Text.RegularExpressions.Regex.Replace(address.PostalCode, "\\D", string.Empty);
            if (cep.Length != 8) return Task.FromResult<(double?, double?)>((null, null));

            if (_map.TryGetValue(cep, out var coords))
            {
                return Task.FromResult<(double?, double?)>((coords.Lat, coords.Lng));
            }

            return Task.FromResult<(double?, double?)>((null, null));
        }
    }
}
