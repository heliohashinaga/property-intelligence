# Provider: ViaCEP

## Fonte
- **URL base**: `https://viacep.com.br`
- **Tipo**: HTTP público, sem autenticação
- **SLA**: Nenhum — serviço voluntário; usar cache agressivo
- **Implementação**: `src/PropertyIntelligence.Providers/ViaCep/ViaCepProvider.cs`

---

## Endpoint

```
GET /ws/{cep}/json/
```

### Parâmetros
| Parâmetro | Tipo   | Descrição                          |
|-----------|--------|------------------------------------|
| `cep`     | string | 8 dígitos sem hífen, ex: `01310100` |

### Exemplo
```
GET https://viacep.com.br/ws/01310100/json/
```

---

## Resposta — sucesso (200)

```json
{
  "cep":        "01310-100",
  "logradouro": "Rua Augusta",
  "complemento": "",
  "bairro":     "Consolação",
  "localidade": "São Paulo",
  "uf":         "SP",
  "ibge":       "3550308",
  "gia":        "1004",
  "ddd":        "11",
  "siafi":      "7107"
}
```

## Resposta — CEP inexistente (200 com flag de erro)

```json
{ "erro": "true" }
```

> ⚠️ **Quirk crítico**: `"erro"` é uma **string** `"true"`, não um boolean `true`.
> Deserializar como `bool` causa falha silenciosa — o campo fica `false` e CEPs
> inexistentes não são detectados. Usar `string? Erro` e comparar com `== "true"`.

## Resposta — formato inválido (400)

CEP com formato inválido (≠ 8 dígitos, alfanumérico ou com espaço).

---

## Mapeamento → domínio (`PropertyAddress`)

| Campo ViaCEP   | Campo domínio                  |
|----------------|--------------------------------|
| `logradouro`   | `StreetName`                   |
| `bairro`       | `Neighborhood`                 |
| `localidade`   | `City`                         |
| `uf`           | `State`                        |
| `cep` (limpo)  | `PostalCode` (sem hífen)       |
| —              | `Lat`, `Lng` (via Nominatim)   |

---

## Geocodificação (Nominatim)

Após resolver o CEP, o provider faz uma segunda chamada ao Nominatim para obter coordenadas:

```
GET https://nominatim.openstreetmap.org/search
    ?q={logradouro}+{localidade}+{uf}+Brasil
    &format=json
    &countrycodes=br
    &limit=1
```

Falha no Nominatim é **silenciosa** — `Lat`/`Lng` ficam `null` e os providers
que dependem de coordenadas (Overpass, ANA, IBGE, CNES, INEP) retornam vazio.

---

## Limitações e quirks

- **Sem número da casa**: retorna apenas intervalos aproximados ("de 32 a 62 – lado par")
- **Limite de 50 resultados** na busca por logradouro (não usada neste provider)
- **Indisponibilidade esporádica**: sem SLA; cache de 30 dias é essencial
- **CEPs de caixa postal** (`20.040-020` etc.) retornam dados incompletos

## Cache
- **TTL**: 30 dias
- **Chave**: `viacep:{SHA256(normalizedAddress)[..16]}`
