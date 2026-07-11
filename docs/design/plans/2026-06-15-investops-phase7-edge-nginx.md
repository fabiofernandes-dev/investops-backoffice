# InvestOps Backoffice — Fase 7 (Edge nginx + Containerização) Implementation Plan

**Goal:** `docker compose up` sobe a stack inteira (nginx + api + migrator + worker + marketdata + infra) num cenário **same-origin** (nginx serve o SPA e faz proxy `/api`), sem CORS, com SSE atravessando o proxy.

**Architecture:** API .NET e migrator containerizados (Dockerfiles, context = raiz do repo p/ enxergar `contracts/proto`). Migrator one-shot (`INVESTOPS_DB`, `--seed`) roda antes da API. nginx serve o build de produção do Angular (`apiBaseUrl: '/api'`) e faz proxy `/api → api:8080` com `proxy_buffering off` (SSE). Dev (`ng serve` + `dotnet run`) fica intacto.

**Tech Stack:** Docker multi-stage (.NET SDK/aspnet/runtime, node:22-alpine, nginx:alpine), docker-compose, Angular fileReplacements.

**Spec:** [docs/design/specs/2026-06-15-investops-phase7-edge-nginx-design.md](../specs/2026-06-15-investops-phase7-edge-nginx-design.md)

---

## Convenções
- Branch `feature/*` a partir de `developer`; 1 task → 1 PR → **pausa**.
- Migrator lê a conn string de **`INVESTOPS_DB`** (console puro; não usa `ConnectionStrings__Postgres`).
- Build da API/migrator: **context = raiz** (precisa de `backend/` + `contracts/proto/`).

---

## Chunk 1: Containerizar API + migrator (7.1)

### Task 7.1: Dockerfiles da API e do migrator + serviços no compose

**Files:**
- Create: `.dockerignore` (raiz), `backend/Dockerfile`, `backend/Dockerfile.migrator`
- Modify: `docker-compose.yml` (serviços `migrator` e `api`)

- [ ] **Step 1: Branch** `feature/api-container`.

- [ ] **Step 2: `.dockerignore` (raiz)**:
  ```
  **/bin
  **/obj
  **/node_modules
  **/dist
  .git
  .angular
  .vs
  .superpowers
  .playwright-mcp
  ```

- [ ] **Step 3: `backend/Dockerfile`** (context = raiz):
  ```dockerfile
  FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
  WORKDIR /src
  COPY backend/ ./backend/
  COPY contracts/proto/ ./contracts/proto/
  RUN dotnet publish backend/src/InvestOps.Api/InvestOps.Api.csproj -c Release -o /app

  FROM mcr.microsoft.com/dotnet/aspnet:8.0
  WORKDIR /app
  COPY --from=build /app ./
  EXPOSE 8080
  ENTRYPOINT ["dotnet", "InvestOps.Api.dll"]
  ```
  > `publish` restaura sozinho. O `<Protobuf>` (`..\..\..\contracts\proto\...`) resolve porque `backend/` e `contracts/` ficam irmãos em `/src`. `Grpc.Tools` gera o client no build.

- [ ] **Step 4: `backend/Dockerfile.migrator`** (context = raiz; runtime console):
  ```dockerfile
  FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
  WORKDIR /src
  COPY backend/ ./backend/
  RUN dotnet publish backend/src/InvestOps.Migrator/InvestOps.Migrator.csproj -c Release -o /app

  FROM mcr.microsoft.com/dotnet/runtime:8.0
  WORKDIR /app
  COPY --from=build /app ./
  ENTRYPOINT ["dotnet", "InvestOps.Migrator.dll"]
  ```
  > Confirmado: o `InvestOps.Migrator` **não** referencia `Infrastructure` (só `dbup-postgresql`), então **não** precisa de `contracts/proto`. Os scripts `.sql` são **`EmbeddedResource`** no DLL (`MigrationRunner` usa `WithScriptsEmbeddedInAssembly`) → vão no publish. `runtime:8.0` basta (console, sem ASP.NET).

- [ ] **Step 5: compose — `migrator`**:
  ```yaml
  migrator:
    build:
      context: .
      dockerfile: backend/Dockerfile.migrator
    command: ["--seed"]
    environment:
      INVESTOPS_DB: Host=postgres;Port=5432;Database=${POSTGRES_DB:-investops};Username=${POSTGRES_USER:-investops};Password=${POSTGRES_PASSWORD:-investops}
    depends_on:
      postgres:
        condition: service_healthy
    restart: "no"
  ```

- [ ] **Step 6: compose — `api`**:
  ```yaml
  api:
    build:
      context: .
      dockerfile: backend/Dockerfile
    environment:
      ASPNETCORE_URLS: http://+:8080
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__Postgres: Host=postgres;Port=5432;Database=${POSTGRES_DB:-investops};Username=${POSTGRES_USER:-investops};Password=${POSTGRES_PASSWORD:-investops}
      RabbitMq__Host: rabbitmq
      MarketData__Url: http://marketdata:5300
    depends_on:
      postgres: { condition: service_healthy }
      rabbitmq: { condition: service_healthy }
      marketdata: { condition: service_started }
      migrator: { condition: service_completed_successfully }
  ```
  > `api` **não** publica porta (acesso via nginx na 7.2). `RabbitMq:Username/Password/Port/Exchange` vêm do `appsettings` (defaults batem com o serviço rabbitmq).

- [ ] **Step 7: Validar** —
  ```bash
  docker compose --env-file .env.example up -d --build postgres rabbitmq marketdata migrator api worker
  docker compose --env-file .env.example logs migrator | tail   # "Migrations concluídas"
  docker compose --env-file .env.example exec -T postgres psql -U investops -d investops -tAc "select count(*) from customers;"  # > 0 (seed rodou)
  docker compose --env-file .env.example ps   # api 'running'; migrator 'exited (0)'
  docker compose --env-file .env.example logs api | tail        # "Application started"
  ```
  Aceite: **migrator semeou** (count > 0) e **api subiu**. (O `/api` via browser é validado na 7.2.) `docker compose down` ao fim.

- [ ] **Step 8: Commit + push + PR** → **PAUSA**.

---

## Chunk 2: Edge nginx + build de produção do SPA (7.2)

### Task 7.2: nginx serve o SPA + proxy /api (same-origin)

**Files:**
- Create: `frontend/investops-web/.dockerignore`, `frontend/investops-web/Dockerfile`, `frontend/investops-web/nginx.conf`, `frontend/investops-web/src/environments/environment.production.ts`
- Modify: `frontend/investops-web/angular.json` (fileReplacements na config `production`), `docker-compose.yml` (serviço `web`)

- [ ] **Step 1: Branch** `feature/edge-nginx` (idealmente após **#33** mergeado, p/ o SPA incluir a tela de Cotações).

- [ ] **Step 2: `environment.production.ts`**:
  ```ts
  export const environment = { apiBaseUrl: '/api' };
  ```

- [ ] **Step 3: `angular.json`** — na configuração `production` do target `build`, adicionar:
  ```json
  "fileReplacements": [
    { "replace": "src/environments/environment.ts", "with": "src/environments/environment.production.ts" }
  ]
  ```
  (mantém os blocos `budgets`/`optimization` existentes). `ng build` (default = production) passa a usar `/api`.

- [ ] **Step 4: `.dockerignore` (frontend)**: `node_modules`, `dist`, `.angular`.

- [ ] **Step 5: `nginx.conf`**:
  ```nginx
  server {
    listen 80;
    server_name _;
    root /usr/share/nginx/html;
    index index.html;

    gzip on;
    gzip_types text/css application/javascript application/json image/svg+xml;

    location /api/ {
      proxy_pass http://api:8080;
      proxy_http_version 1.1;
      proxy_set_header Host $host;
      proxy_set_header Connection "";
      proxy_buffering off;     # SSE: sem buffer
      proxy_cache off;
      proxy_read_timeout 3600s;
    }

    location / {
      try_files $uri $uri/ /index.html;   # rotas client-side do Angular
    }
  }
  ```
  > `proxy_pass http://api:8080;` **sem barra/caminho final** → preserva o prefixo `/api` (as rotas são `api/...`).

- [ ] **Step 6: `frontend/investops-web/Dockerfile`** (context = `frontend/investops-web`):
  ```dockerfile
  FROM node:22-alpine AS build
  WORKDIR /app
  COPY package*.json ./
  RUN npm ci
  COPY . .
  RUN npm run build

  FROM nginx:alpine
  COPY nginx.conf /etc/nginx/conf.d/default.conf
  COPY --from=build /app/dist/investops-web/browser /usr/share/nginx/html
  ```

- [ ] **Step 7: compose — `web`**:
  ```yaml
  web:
    build: ./frontend/investops-web
    ports:
      - "${WEB_PORT:-8080}:80"
    depends_on:
      - api
  ```

- [ ] **Step 8: Smoke same-origin (compose + Playwright)** —
  ```bash
  docker compose --env-file .env.example up -d --build
  curl -s --retry 20 --retry-delay 2 --retry-all-errors -o /dev/null -w "web: %{http_code}\n" http://localhost:8080
  curl -s --retry 20 --retry-delay 2 --retry-all-errors -o /dev/null -w "api via nginx: %{http_code}\n" "http://localhost:8080/api/operations?pageSize=1"
  ```
  (retry cobre o cold start da api/502 inicial; comandos via Bash tool, não PowerShell)
  Playwright: abrir `http://localhost:8080` → SPA carrega; navegar /operations (dados), /quotes (ticker SSE atualizando **através do nginx**); confirmar no devtools/network que as chamadas são **same-origin** (`localhost:8080/api/...`, sem CORS). `docker compose down` ao fim.

- [ ] **Step 9: Commit + push + PR** → **PAUSA**.

---

## Fechamento da Fase 7
`docker compose up` entrega o sistema inteiro em **mesma origem** (`http://localhost:8080`) — equivalente local do edge (nginx) que em nuvem seria CloudFront/ALB. Fecha o gap do SPEC §14 (tudo no compose). Dev segue com `ng serve`+`dotnet run`.
