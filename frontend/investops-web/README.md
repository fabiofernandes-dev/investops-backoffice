# InvestOps Web

SPA Angular do backoffice InvestOps. Consome a API .NET em `/api` e exibe a lista de operações, o dashboard de métricas e o reprocessamento de operações que caíram na DLQ.

## Stack

- Angular 21 **zoneless** (standalone components, signals, `OnPush`, sem zone.js) + Angular Material
- TypeScript, RxJS
- Jest (testes unitários)

## Desenvolvimento

```bash
npm install
npm start          # ng serve → http://localhost:4200
```

Em dev o SPA chama a API em `http://localhost:5000/api` (definido em `src/environments/environment.ts`). Suba a API antes — veja o [README raiz](../../README.md#5-api-net).

## Build

```bash
npm run build      # artefatos de produção em dist/
```

Em produção o SPA usa `apiBaseUrl: '/api'` e é servido por **nginx**, que faz proxy de `/api` para a API (same-origin). Veja `Dockerfile` e `nginx.conf`.

## Testes

```bash
npm test           # Jest
```
