# Design — InvestOps Backoffice (Fase 7: Edge nginx + containerização full)

**Data:** 2026-06-15
**Status:** Aprovado (início da Fase 7)
**Contexto:** tornar o `docker compose up` um cenário **production-like de mesma origem**: um **nginx** na frente serve o SPA Angular e faz **proxy `/api`** para a API .NET (containerizada). Sem CORS, com SSE atravessando o proxy.

---

## 1. Decisões (confirmadas)

| Tema | Decisão |
|------|---------|
| Topologia | **Containerizar tudo**: Dockerfile da API + serviço `api`; nginx (`web`) serve o build Angular + proxy `/api → api` |
| Migrations | **Serviço `migrator` one-shot** (depends_on postgres healthy, aplica schema **+ seed**); a `api` espera concluir (`service_completed_successfully`) |
| Origem | **Mesma origem** via nginx → **sem CORS** em produção (CORS continua só em Development, para `ng serve`) |
| Build do SPA | `environment.production.ts` com `apiBaseUrl: '/api'` (relativo) + `fileReplacements` no `angular.json` |
| Dev | Fluxo de dev **inalterado**: `ng serve` (4200) + `dotnet run` (5000) seguem usando `environment.ts` (`http://localhost:5000/api`) e CORS |
| Acesso | `http://localhost:8080` (nginx) é a única porta pública; a `api` **não** é publicada no host (só na rede do compose) |

## 2. Topologia (compose)

```text
Browser ─► localhost:8080  [web: nginx]
                 ├─ /            → SPA Angular (estático, try_files → index.html)
                 └─ /api/        → proxy → [api:8080] .NET
                                              ├─ Postgres, RabbitMQ
                                              └─ gRPC → [marketdata:5300]
[migrator] (one-shot) ─► aplica schema+seed antes da api subir
[worker] consome RabbitMQ (já existe)
```

## 3. Containerização da API

- **`backend/Dockerfile`** (multi-stage), **build context = raiz do repo** (a API precisa do `.proto` em `contracts/proto`, fora de `backend/`):
  - `mcr.microsoft.com/dotnet/sdk:8.0` → `dotnet restore`/`publish` da solução; runtime `mcr.microsoft.com/dotnet/aspnet:8.0`.
  - `COPY backend/ ./backend/` + `COPY contracts/proto/ ./contracts/proto/` (preserva o caminho relativo `..\..\..\contracts` do `<Protobuf>`); `Grpc.Tools` gera o client no build.
  - Publica `InvestOps.Api`; `ENTRYPOINT ["dotnet","InvestOps.Api.dll"]`.
- **Serviço `api`** (env, apontando para nomes de serviço do compose):
  - `ASPNETCORE_URLS=http://+:8080`, `ASPNETCORE_ENVIRONMENT=Production` (sem Swagger, **sem CORS** — mesma origem),
  - `ConnectionStrings__Postgres=Host=postgres;...`, `RabbitMq__Host=rabbitmq`, `MarketData__Url=http://marketdata:5300`.
  - `depends_on`: postgres `service_healthy`, rabbitmq `service_healthy`, marketdata `service_started` (não tem healthcheck), **migrator `service_completed_successfully`**.
  - **Não** publica porta no host (acesso só via nginx).
- **`.dockerignore` (raiz)**: excluir `**/bin`, `**/obj`, `**/node_modules`, `**/dist`, `.git`, `.angular`, `.vs` — o context da API é a raiz; sem isso, manda `node_modules` (~400MB) e `bin/obj` para o build (lento + risco de artefato velho).

## 4. Migrator (one-shot)

- **`backend/Dockerfile.migrator`** (multi-stage) publica `InvestOps.Migrator`; `ENTRYPOINT ["dotnet","InvestOps.Migrator.dll"]`.
- Serviço `migrator`: `command: ["--seed"]`, env **`INVESTOPS_DB=Host=postgres;Port=5432;Database=investops;Username=investops;Password=investops`**, `depends_on: postgres healthy`, `restart: "no"` (roda uma vez). DbUp é idempotente.
  > ⚠️ O `InvestOps.Migrator` é console puro (sem `IConfiguration`): resolve a connection string por **arg posicional → env `INVESTOPS_DB` → fallback `localhost`**. **Não** lê `ConnectionStrings__Postgres` (isso é só da API). Por isso usamos `INVESTOPS_DB` (senão cairia em `localhost` dentro do container e falharia). Aceite da task: verificar uma **linha semeada** no banco (não só exit 0).

## 5. nginx (`web`)

- **`frontend/investops-web/Dockerfile`** (multi-stage; context = `frontend/investops-web`, self-contained):
  - `node:22-alpine` → `npm ci && npm run build` (produção; usa `environment.production.ts`).
  - `nginx:alpine` ← copia `dist/investops-web/browser` → `/usr/share/nginx/html`; copia `nginx.conf`.
  - **`.dockerignore` (frontend)**: excluir `node_modules`, `dist`, `.angular` (senão o `node_modules` do host entra no context e pode mascarar o `npm ci`).
- **`nginx.conf`**:
  - `location / { try_files $uri $uri/ /index.html; }` (rotas client-side do Angular).
  - `location /api/ { proxy_pass http://api:8080; proxy_http_version 1.1; proxy_set_header Host $host; proxy_set_header Connection ''; proxy_buffering off; proxy_cache off; proxy_read_timeout 3600s; }` — **`proxy_buffering off` + timeout longo** para o SSE funcionar (é o par do nosso `X-Accel-Buffering: no`).
  - `gzip on` para os estáticos.
- Serviço `web`: `build ./frontend/investops-web`, `ports: ["8080:80"]`, `depends_on: api`.

## 6. Build de produção do Angular

- `src/environments/environment.production.ts`: `export const environment = { apiBaseUrl: '/api' };`.
- `angular.json` (configuração `production`): `fileReplacements` de `environment.ts` → `environment.production.ts`.
- `ng serve` (dev) continua com `environment.ts` (`http://localhost:5000/api`). O Docker roda `ng build` (production por padrão) → `/api` relativo → **mesma origem**.

## 7. Erros / bordas
- **SSE atrás do nginx**: exige `proxy_buffering off` (feito) e timeout longo; senão o ticker "engasga".
- **api não pronta**: nginx sobe antes da api ficar pronta → `/api` pode dar 502 por alguns segundos; aceitável (sem healthcheck na api porque a imagem aspnet não traz curl). O SPA carrega; as chamadas reconectam.
- **CORS**: em Production não há política CORS — e não precisa (mesma origem). Em dev, intacto.

## 8. Quebra em tasks (PRs)

| # | Branch | Entrega |
|---|--------|---------|
| 7.1 | `feature/api-container` | `backend/Dockerfile` (API) + `Dockerfile.migrator` + serviços `api` e `migrator` no compose; validar migrations + `/api` interno |
| 7.2 | `feature/edge-nginx` | `environment.production.ts` + fileReplacements + `frontend/.../Dockerfile` + `nginx.conf` + serviço `web`; **smoke same-origin** (`http://localhost:8080`: SPA + `/api` sem CORS + ticker SSE) |

**Ordem:** 7.1 → 7.2. Ideal mergear **#33 (tela Cotações)** antes do build de prod, para o SPA incluir o ticker no smoke; não é bloqueante para a infra.

## 9. Fora de escopo
- TLS/HTTPS no nginx (cert/Let's Encrypt) — dev é HTTP; em nuvem o TLS termina no CDN/LB.
- CDN/ALB/cloud real (CloudFront/ALB) — isto é o equivalente **local** (nginx) do edge.
- mTLS no gRPC interno; healthcheck custom da api.
- Otimização de imagem (trimming/AOT do .NET).
