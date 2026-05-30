# Provider: Nominatim (OpenStreetMap)

## Fonte
- **URL base**: `https://nominatim.openstreetmap.org`
- **Tipo**: HTTP público, sem autenticação
- **ToS**: Exige `User-Agent` identificável; máximo ~1 req/s por IP
- **Implementação**: usado por `ViaCepProvider` e `AddressNormalizerService`

---

## Endpoint

```
GET /search
```

### Parâmetros relevantes
| Parâmetro      | Valor           | Descrição                                    |
|----------------|-----------------|----------------------------------------------|
| `q`            | string          | Endereço em texto livre                      |
| `format`       | `json`          | Formato da resposta                          |
| `countrycodes` | `br`            | Restringe ao Brasil                          |
| `addressdetails` | `1`           | Inclui breakdown do endereço por componente  |
| `limit`        | `1`–`5`         | Número máximo de resultados                  |

### Exemplos
```
# Geocodificação livre (AddressNormalizerService)
GET /search?q=Rua+Augusta+1500+São+Paulo&format=json&countrycodes=br&addressdetails=1&limit=5

# Geocodificação por CEP (ViaCepProvider)
GET /search?q=Rua+Augusta+Consolação+SP+Brasil&format=json&countrycodes=br&limit=1
```

---

## Resposta (array JSON)

```json
[
  {
    "place_id": 123456,
    "lat": "-23.5563",
    "lon": "-46.6543",
    "display_name": "Rua Augusta, 1500, Consolação, São Paulo, SP, 01310-100, Brasil",
    "type": "house",
    "address": {
      "house_number": "1500",
      "road":         "Rua Augusta",
      "suburb":       "Consolação",
      "city":         "São Paulo",
      "state":        "São Paulo",
      "postcode":     "01310-100",
      "country_code": "br"
    }
  }
]
```

> **Atenção**: `lat` e `lon` são **strings**, não números. Usar
> `double.TryParse(..., CultureInfo.InvariantCulture, ...)` para converter.

---

## Mapeamento → domínio (`PropertyAddress`)

| Campo Nominatim          | Campo domínio         | Fallback                         |
|--------------------------|-----------------------|----------------------------------|
| `lat`                    | `Lat`                 | —                                |
| `lon`                    | `Lng`                 | —                                |
| `address.road`           | `StreetName`          | —                                |
| `address.house_number`   | `StreetNumber`        | —                                |
| `address.suburb`         | `Neighborhood`        | `address.neighbourhood`          |
| `address.city`           | `City`                | `address.town` → `address.municipality` |
| `address.state`          | `State`               | —                                |
| `address.postcode`       | `PostalCode`          | remove `-`                       |
| `display_name`           | `NormalizedAddress`   | —                                |

---

## Limitações e quirks

- **Resultado vazio** para endereços novos ou com nomenclatura não-OSM → retornar `null` (422)
- **`address.city` pode ser `null`** em municípios menores; usar `town` ou `municipality` como fallback
- **`state`** retorna o nome completo ("São Paulo"), não a sigla ("SP") — o código atual usa como-está
- **Rate limit não-oficial**: ~1 req/s por IP; o pipeline de resiliência limita a 1 retry para evitar ban
- **Precisão**: geocodificação por logradouro sem número pode retornar centróide da rua

## Cache
Nominatim não tem cache próprio no provider — o cache é gerenciado pelo `ViaCepProvider`
e pelo `AddressNormalizerService` upstream (TTL herdado do contexto de uso).
