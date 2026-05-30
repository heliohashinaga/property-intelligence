# Provider: IPTU API (iptuapi.com.br)

## Fonte
- **URL base**: `https://api.iptuapi.com.br`
- **Tipo**: HTTP com autenticação Bearer
- **Cobertura**: 7 capitais brasileiras — São Paulo, Belo Horizonte, Rio de Janeiro, Recife, e outras
- **Plano**: Free tier com limite de quota
- **Implementação**: `src/PropertyIntelligence.Providers/Iptu/IptuApiProvider.cs`

---

## Autenticação

```
Authorization: Bearer {IPTU_API_KEY}
```

> ⚠️ **Não usar `?api_key=` como query param** — o endpoint rejeita silenciosamente.
> A chave vai **exclusivamente** no header `Authorization: Bearer`.

Configuração via env var: `IPTU_API_KEY=<chave>`

---

## Endpoint

```
GET /v1/imoveis/busca
```

### Parâmetros
| Parâmetro  | Obrigatório | Descrição                                   |
|------------|-------------|---------------------------------------------|
| `endereco` | ✅           | Logradouro + número, ex: `Rua Augusta 1500` |
| `cidade`   | ✅           | Slug da cidade, ex: `sao-paulo`             |

### Slugs de cidade suportados
| Cidade          | Slug              |
|-----------------|-------------------|
| São Paulo       | `sao-paulo`       |
| Belo Horizonte  | `belo-horizonte`  |
| Rio de Janeiro  | `rio-de-janeiro`  |
| Recife          | `recife`          |

### Exemplo
```
GET /v1/imoveis/busca?endereco=Rua%20Augusta%201500&cidade=sao-paulo
Authorization: Bearer sk_live_xxxx
```

---

## Resposta — sucesso (200)

```json
{
  "valor_venal":  850000.00,
  "zoneamento":   "ZM-3a",
  "historico": [
    { "ano": 2024, "valor_venal": 850000.00 },
    { "ano": 2023, "valor_venal": 810000.00 },
    { "ano": 2022, "valor_venal": 775000.00 }
  ]
}
```

## Resposta — não encontrado (404)

Imóvel não cadastrado ou endereço ambíguo. Retorno: `null` gracioso (não lança exceção).
O campo `PropertyProfile.IptuData` fica `null` e a dimensão `appreciation` usa
regra neutra (+50 pts).

---

## Mapeamento → domínio (`IptuData`)

| Campo API       | Campo domínio               | Notas                              |
|-----------------|-----------------------------|------------------------------------|
| `valor_venal`   | `ValorVenal` (decimal?)     | Valor em BRL                       |
| `zoneamento`    | `ZoningClass` (string?)     | Ex: `ZM-3a`, `ZEU`, `ZC`, `ZOE`   |
| `historico`     | *(não mapeado no MVP)*      | Reservado para CAGR em US2/Phase 4 |

### Zoneamentos de interesse para `opportunity_flags`
| Zoneamento     | Flag gerada           |
|----------------|-----------------------|
| `ZEU`, `ZOE`   | `zoning_upscale`      |
| `ZC`, `ZM-3*`  | `zoning_upscale`      |

---

## Normalização da cidade

O provider converte o nome da cidade para slug automaticamente:

```
"São Paulo"      → "sao-paulo"
"Belo Horizonte" → "belo-horizonte"
"Rio de Janeiro" → "rio-de-janeiro"
```

Cidades não cobertas retornam 404 → `null` gracioso (não é falha de provider).

---

## Quirks e limitações

- **MVP**: `historico` não é mapeado — CAGR será calculado em US2 (Phase 4)
- **Quota free tier**: limite não publicado; circuit breaker configurado (3 falhas/60s → 30s de abertura)
- **Endereço ambíguo**: múltiplos imóveis no mesmo endereço → API retorna o primeiro match
- **Cidades fora da cobertura**: retorna 404, tratado como "dado indisponível" (não erro)

## Resiliência
- **Retry**: 1x, delay constante 1s (somente 5xx; 404 **não** é retried — é resultado válido)
- **Circuit breaker**: 60s janela, 60% falha, 3 mínimo, 30s break
- **Timeout por tentativa**: 3s

## Cache
- **TTL**: 30 dias
- **Chave**: `iptu_api:{SHA256(normalizedAddress)[..16]}`
