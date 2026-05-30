# Incident Response Plan — Property Intelligence

> **Required gate for production deploy (T073).** This document must be committed
> before the K3s deploy job (`T073`) runs. The CI pipeline checks its existence.

**Version**: 1.0 | **Last Updated**: 2026-05-30
**Legal basis**: LGPD Art. 48; Resolução CD/ANPD nº 15/2024
**DPO Contact**: privacidade@[domínio]

---

## 1. Definitions

| Term | Definition |
|---|---|
| **Incidente de segurança** | Acesso não autorizado, vazar, alterar, destruir ou divulgar dados pessoais sem autorização |
| **Titular** | Pessoa natural a quem se referem os dados pessoais tratados |
| **Controlador** | Property Intelligence — determina as finalidades e meios do tratamento |
| **ANPD** | Autoridade Nacional de Proteção de Dados |

---

## 2. Risk Classification

| Nível | Critérios | Notificação ANPD |
|---|---|---|
| **Baixo** | Dados não sensíveis, sem risco relevante ao titular, acesso interno | Não obrigatória |
| **Médio** | Dados pessoais expostos, risco potencial aos titulares, acesso externo não autorizado | Avaliar necessidade |
| **Alto** | Dados expostos em larga escala, risco de dano concreto, credenciais vazadas | **Obrigatória em até 3 dias úteis** |

---

## 3. Response Steps

```
Detect → Triage → Contain → Notify (ANPD + titulares) → Remediate → Post-mortem
```

### 3.1 Detect & Triage (0–2 h)
- [ ] Identificar a natureza do incidente (tipo de dado, vetor de ataque, escopo)
- [ ] Estimar volume de titulares afetados
- [ ] Classificar risco (Baixo / Médio / Alto)
- [ ] Abrir registro de incidente (ver template §6)

### 3.2 Contain (2–12 h)
- [ ] Revogar credenciais comprometidas (`API_KEY_SALT`, `OPENROUTER_API_KEY`, etc.)
- [ ] Bloquear IPs suspeitos via Cloudflare WAF
- [ ] Desativar endpoints afetados se necessário (`kubectl rollout undo`)
- [ ] Preservar evidências (logs, snapshots de banco) antes de qualquer limpeza

### 3.3 Notify ANPD — **prazo: 3 dias úteis** (Resolução CD/ANPD nº 15/2024)
- Acessar portal: https://www.gov.br/anpd/pt-br/assuntos/incidentes-de-seguranca
- Preencher formulário com dados do §4 abaixo
- Guardar número de protocolo

### 3.4 Notify Titulares Afetados
- Comunicar por e-mail ou aviso no serviço quando houver risco relevante
- Incluir: o que aconteceu, quais dados, medidas tomadas, canal de contato

### 3.5 Remediate
- [ ] Corrigir vulnerabilidade identificada
- [ ] Rotacionar todas as secrets potencialmente expostas
- [ ] Aplicar patch e re-deploy (`kubectl set image ...`)
- [ ] Verificar integridade dos dados afetados

### 3.6 Post-mortem (até 5 dias após contenção)
- Documento com: timeline, causa-raiz, impacto, lições aprendidas, ações preventivas

---

## 4. ANPD Notification Template

```
RELATÓRIO DE INCIDENTE DE SEGURANÇA — LGPD Art. 48

Data/Hora de Detecção:   YYYY-MM-DD HH:MM UTC
Data/Hora de Ocorrência: YYYY-MM-DD HH:MM UTC (estimada)

Controlador:      Property Intelligence
CNPJ:             [CNPJ]
Encarregado (DPO): [nome] — privacidade@[domínio]

Natureza do incidente:
  [ ] Acesso não autorizado
  [ ] Vazar dados (exfiltração)
  [ ] Alteração não autorizada
  [ ] Destruição/perda de dados
  [ ] Outro: _____________

Categorias de dados pessoais afetados:
  [ ] Endereço/localização
  [ ] Identificadores (API Key)
  [ ] Dados de acesso (IP, logs)
  [ ] Outro: _____________

Número estimado de titulares afetados: ___________

Medidas de contenção já adotadas:
  [descrever]

Medidas planejadas para eliminação do risco:
  [descrever]

O incidente afeta dados de menores? [ ] Sim  [ ] Não
O incidente afeta dados sensíveis (Art. 5º, II)?  [ ] Sim  [ ] Não
```

---

## 5. Incident Log

Manter planilha em `docs/incidents/YYYY-MM-DD-incident.md` para cada incidente:

| Campo | Valor |
|---|---|
| ID do Incidente | INC-YYYY-MM-DD-001 |
| Data de Detecção | YYYY-MM-DD HH:MM UTC |
| Classificação | Baixo / Médio / Alto |
| Dados Afetados | [tipos] |
| Titulares Afetados (estimativa) | [número] |
| ANPD Notificada? | Sim / Não / N/A |
| Protocolo ANPD | [número] |
| Data de Contenção | YYYY-MM-DD |
| Status | Aberto / Contido / Remediado / Encerrado |

---

## 6. Contacts

| Papel | Contato |
|---|---|
| DPO / Encarregado | privacidade@[domínio] |
| On-call Técnico | [preencher antes do deploy] |
| ANPD | anpd.gov.br/portal-titular |
| Cloudflare Support | support.cloudflare.com |

---

*Este documento deve ser revisado anualmente ou após qualquer incidente de nível Médio/Alto.*
