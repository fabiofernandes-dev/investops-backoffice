# Contribuindo

Obrigado pelo interesse! Este é um projeto de portfólio, mas issues e PRs são bem-vindos.

## Pré-requisitos

- [Docker](https://www.docker.com/) + Docker Compose
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — API, Migrator, testes
- [Go 1.26+](https://go.dev/dl/) — worker e marketdata
- [Node.js 20+](https://nodejs.org/) — frontend Angular

Como rodar a stack (Docker ou componente a componente) está no [README — Como rodar](README.md#como-rodar).

## Testes (obrigatório antes do PR)

```bash
cd backend && dotnet test                 # .NET — unit + integração (Testcontainers exige Docker)
cd worker-go && go test ./...             # worker Go
cd marketdata-go && go test ./...         # marketdata Go
cd frontend/investops-web && npm test     # Angular (Jest)
```

## Fluxo de trabalho

- `main` — estável (release por fase); `developer` — integração; `feature/*` — uma branch por tarefa.
- Abra o PR contra **`developer`**. Todo o CI (build + testes das 4 stacks) precisa estar **verde**.
- Commits seguem [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `chore:`, `docs:`…).

## Estilo de código

- Comente apenas o **não óbvio** (o *porquê* de uma decisão, workaround, algoritmo) — evite comentar o que o código já diz.
- Métodos pequenos e focados; nomes descritivos; SQL parametrizado.
- Decisões de arquitetura relevantes viram um **ADR** em [`docs/adr/`](docs/adr/) (siga o formato dos existentes).
- Siga o estilo do código ao redor; não reformate o que não está tocando.
