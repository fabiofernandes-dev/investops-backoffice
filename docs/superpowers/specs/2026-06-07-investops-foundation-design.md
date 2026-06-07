# Design — InvestOps Backoffice (Fundação e Roadmap)

**Data:** 2026-06-07
**Status:** Aprovado (início da Fase 1)
**Spec de produto:** [SPEC.md](../../../SPEC.md)

Este documento captura *como* vamos construir o InvestOps Backoffice. O **quê** (domínio, regras, endpoints, telas) está totalmente descrito no `SPEC.md`; aqui registramos convenções, decisões de processo e a quebra em tasks.

---

## 1. Modo de trabalho

- **Implementação:** Claude implementa; o usuário (fabiofernandes-dev) revisa cada PR para aprender e conseguir explicar o sistema numa entrevista.
- **Cadência:** fase a fase. Ao fim de cada fase, reavaliamos antes de seguir.
- **Objetivo:** treinar para vaga sênior fullstack. Prioridade é clareza arquitetural e capacidade de explicar decisões (`SPEC.md` §23), não completar 100% das features.

## 2. Convenções de Git

| Branch | Papel |
|--------|-------|
| `main` | Estável. Recebe merge de `developer` ao fechar uma fase. |
| `developer` | Integração contínua. Default branch no GitHub. |
| `feature/*` | Uma branch por task, criada a partir de `developer`. |

- Fluxo: `feature/*` → **PR** → `developer` (revisão do usuário) → merge.
- `developer` → `main` ao concluir uma fase (release da fase).
- **Conventional Commits** (`feat:`, `fix:`, `chore:`, `test:`, `docs:`, `refactor:`).
- Remote: `investops-backoffice` **privado** na conta `fabiofernandes-dev`.

## 3. Decisões técnicas

| Decisão | Escolha | Motivo |
|---------|---------|--------|
| Versão .NET | **net8.0**, SDK fixado via `global.json` (8.0.421) | Fiel ao SPEC e à vaga; SDK 8 está instalado. |
| Migrations | **DbUp** (scripts SQL versionados, migrator .NET) | Idiomático com Dapper (sem EF), versiona evolução, prática sênior. |
| Acesso a dados | **Dapper** | Exigência do SPEC; controle de SQL e performance. |
| Banco | **PostgreSQL compartilhado** entre API e Worker no MVP | SPEC §3; separação por responsabilidade/contratos de evento. |
| Line endings | LF no repo, CRLF para scripts Windows (`.gitattributes`) | Dev em Windows + containers Linux. |

## 4. Roadmap por fases (SPEC §20)

1. **Fase 1 — Fundação** ← *em execução* (infra + esqueleto .NET, sem fluxo de negócio)
2. **Fase 2 — Primeiro fluxo** (importação de operação, validação, idempotência, Outbox, testes)
3. **Fase 3 — Worker Go** (consumo RabbitMQ, regras de negócio, atualização de posição, eventos)
4. **Fase 4 — Angular** (shell, operações, detalhe, posições, integração real)
5. **Fase 5 — Qualidade sênior** (logs estruturados, correlationId, retry, DLQ, auditoria, ADRs, testes)

Cada fase terá seu próprio plano de implementação detalhado.

## 5. Quebra da Fase 1 (Fundação)

Sem nenhum fluxo de negócio (SPEC §22). Cada task é um PR pequeno e revisável.

| # | Branch | Entrega | Pronto quando |
|---|--------|---------|---------------|
| T0 | `chore/repo-bootstrap` | git init, `main`+`developer`, remote GitHub, `.gitignore`/`.editorconfig`/`.gitattributes`, README inicial, estrutura de pastas | repo no GitHub, branches criadas, `developer` default |
| T1 | `feature/infra-compose` | `docker-compose.yml` com PostgreSQL + RabbitMQ Management (AMQP `5672` + UI `15672`), volumes, healthchecks, `.env.example` | `docker compose up` sobe ambos; RabbitMQ AMQP `:5672` e UI `:15672`; Postgres `:5432` |
| T2 | `feature/backend-solution` | `InvestOps.sln` + 5 projetos src + 2 de teste; referências; `Directory.Build.props`; `global.json` | `dotnet build` limpo |
| T3 | `feature/api-foundation` | Serilog + Swagger + Health Checks (liveness + readiness Postgres) + connection strings em `appsettings` | `GET /health` Healthy; Swagger abre; teste de integração do `/health` |
| T4 | `feature/domain-model` | Enums + entidades do SPEC §5 no `Domain` | build/testes passam; entidades só no `Domain` |
| T5 | `feature/db-migrations` | Scripts SQL das 10 tabelas (§10) + todos os índices, via DbUp | migrations aplicam em Postgres limpo; índices conferidos |
| T6 | `feature/docs-foundation` | README "rodar localmente" + ADRs 0001–0005 | critérios de aceite do SPEC §22 atendidos |

**Ordem:** T0 → T1 → T2 → T3 → T4 → T5 → T6.

## 6. Critérios de aceite da Fase 1 (SPEC §22)

- [ ] Estrutura de pastas clara
- [ ] Backend compila
- [ ] `docker-compose` sobe PostgreSQL e RabbitMQ
- [ ] API responde `/health`
- [ ] Swagger abre
- [ ] Entidades no domínio, não no controller
- [ ] Conexão com banco via `appsettings`
- [ ] README explica como rodar
- [ ] Nada fora do escopo da fase (sem telas/serviços de negócio)

## 7. Notas

- Go atualizado para `windows/amd64` (64-bit) — pronto para a Fase 3.
- Sem autenticação complexa no MVP (SPEC §15).
- Os contratos JSON de evento (`contracts/events/*.v1.json`, SPEC §11) ficam para a **Fase 2** (chegam junto do Outbox). Na Fase 1 a pasta existe apenas como placeholder.
