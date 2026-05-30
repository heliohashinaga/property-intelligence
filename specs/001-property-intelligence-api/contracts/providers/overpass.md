# Provider: Overpass API (OpenStreetMap POIs)

## Fonte
- **URL base**: `https://overpass-api.de`
- **Tipo**: HTTP público, sem autenticação
- **ToS**: Uso razoável; não usar para scraping massivo
- **Implementação**: `src/PropertyIntelligence.Providers/Overpass/OverpassPoiProvider.cs`

---

## Endpoint

```
POST /api/interpreter
Content-Type: application/x-www-form-urlencoded

data={overpass_ql_query}
```

---

## Query Overpass QL usada

```overpassql
[out:json][timeout:25];
(
  node["railway"="subway"](around:2000,{lat},{lng});
  way["railway"="subway"](around:2000,{lat},{lng});
  node["railway"="station"]["station"="subway"](around:2000,{lat},{lng});
  way["railway"="station"]["station"="subway"](around:2000,{lat},{lng});
  node["public_transport"="platform"]["train"!="yes"](around:2000,{lat},{lng});
  node["highway"="bus_stop"](around:2000,{lat},{lng});
  node["public_transport"="stop_position"](around:2000,{lat},{lng});
  node["amenity"="hospital"](around:2000,{lat},{lng});
  way["amenity"="hospital"](around:2000,{lat},{lng});
  node["amenity"="school"](around:2000,{lat},{lng});
  way["amenity"="school"](around:2000,{lat},{lng});
  node["amenity"="pharmacy"](around:2000,{lat},{lng});
  node["amenity"="clinic"](around:2000,{lat},{lng});
  node["amenity"="supermarket"](around:2000,{lat},{lng});
  way["amenity"="supermarket"](around:2000,{lat},{lng});
  node["leisure"="park"](around:2000,{lat},{lng});
  way["leisure"="park"](around:2000,{lat},{lng});
);
out center;
```

> `out center` é obrigatório para `way` — retorna o centróide da geometria no campo `center`.
> Sem ele, `way` não teria `lat`/`lon` e seria ignorado no CountPois.

---

## Resposta

```json
{
  "version": 0.6,
  "generator": "Overpass API",
  "elements": [
    {
      "type": "node",
      "id": 123456,
      "lat": -23.5563,
      "lon": -46.6543,
      "tags": { "railway": "subway", "name": "Consolação" }
    },
    {
      "type": "way",
      "id": 789012,
      "center": { "lat": -23.558, "lon": -46.657 },
      "tags": { "amenity": "hospital", "name": "Hospital das Clínicas" }
    }
  ]
}
```

---

## Mapeamento → domínio (`PoiData`)

A contagem é feita com **Haversine** — cada elemento é classificado pela distância real ao ponto de origem:

| Tag OSM                                            | Classificação              | Bucket       |
|----------------------------------------------------|----------------------------|--------------|
| `railway=subway` ou `railway=station station=subway` ou `public_transport=platform` | Transit | 500m / 1km |
| `highway=bus_stop` ou `public_transport=stop_position` | Transit               | 500m / 1km   |
| `amenity=supermarket`                              | Supermarkets               | 1km          |
| `amenity=pharmacy`                                 | Pharmacies                 | 1km          |
| `leisure=park`                                     | Parks                      | 1km          |
| Qualquer tag (todos os acima)                      | Pois2km                    | 2km          |

| Campo `PoiData`    | Critério                         |
|--------------------|----------------------------------|
| `TransitStops500m` | isTransit AND dist ≤ 500m        |
| `TransitStops1km`  | isTransit AND dist ≤ 1000m       |
| `Pois2km`          | qualquer elemento AND dist ≤ 2km |
| `Supermarkets1km`  | amenity=supermarket AND dist ≤ 1km |
| `Pharmacies1km`    | amenity=pharmacy AND dist ≤ 1km  |
| `Parks1km`         | leisure=park AND dist ≤ 1km      |

---

## Quirks e decisões de design

### Metrô de São Paulo
O metrô SP usa **múltiplas tags OSM** dependendo do mapeador:

| Tag                                    | Cobertura          |
|----------------------------------------|--------------------|
| `railway=subway`                       | ✅ Maioria das linhas |
| `railway=station` + `station=subway`  | ✅ Estações maiores  |
| `public_transport=platform` (+ `train!=yes`) | ✅ Plataformas   |
| `railway=subway_entrance`              | ❌ Não incluído (entradas, não a estação) |

Usar apenas `station=subway` captura ~60% das estações SP. As três tags combinadas cobrem ~95%.

### `way` vs `node`
Hospitais e escolas grandes são mapeados como `way` (polígono), não `node`.
Sem `out center`, o `way` não tem `lat`/`lon` e seria descartado.

### Requisito de coordenadas
Se `address.Lat` ou `address.Lng` for `null` (Nominatim falhou), o provider retorna
`PoiData()` vazio sem fazer chamada HTTP — sem exceção.

## Cache
- **TTL**: 7 dias
- **Chave**: `overpass:{SHA256(normalizedAddress)[..16]}`
