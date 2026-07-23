# 6. Autenticação fora do escopo do MVP; rate limiting nativo na API

Status: Accepted

## Contexto

O InvestOps é um backoffice interno: em produção real ficaria atrás de rede privada/VPN, com identidade fornecida por um IdP corporativo (OIDC/SAML) — nunca com user store próprio. O [SPEC.md](../../SPEC.md) restringe explicitamente: "Não implementar autenticação complexa no MVP". Por outro lado, a API expõe endpoints de escrita com efeito colateral (import, reprocess) e um stream SSE de cotações, sem nenhuma proteção contra abuso.

## Decisão

1. **Autenticação/autorização ficam fora do MVP.** A decisão é registrada aqui em vez de implementada: um login JWT com usuários seedados adicionaria atrito à demo (`docker compose up` → usar) sem demonstrar nada além de boilerplate. O design acomoda a evolução: auth entraria como `AddAuthentication` + `[Authorize]` por controller, com policies por perfil (operador × supervisor) mapeadas de claims do IdP.
2. **Rate limiting entra no MVP**, com o middleware nativo do ASP.NET Core 8 (`AddRateLimiter`), sem dependência externa:
   - **Limite global** (fixed window, padrão 100 req/min) para toda a API, particionado por IP de origem.
   - **Política `writes`** mais rígida (padrão 20 req/min) nos endpoints de efeito colateral: `POST /api/operations/import` e `POST /api/operations/{id}/reprocess`.
   - **Health checks isentos** — probes de liveness/readiness não podem ser rejeitadas por limite.
   - Limites configuráveis por ambiente via seção `RateLimiting` do appsettings; rejeição com `429` + header `Retry-After`.

## Consequências

- **Prós:** demo continua friccionless; superfície de abuso dos endpoints de escrita é contida; a distinção leitura × escrita documenta o raciocínio de risco por endpoint.
- **Contras:** API aberta — qualquer cliente com acesso de rede lê e importa operações. Aceitável apenas porque o deploy alvo é local/demonstração.
- **Limitação conhecida:** atrás do edge nginx todas as requisições chegam com o IP do proxy, então o particionamento por IP degrada para um bucket único (limite agregado — conservador, não inseguro). A evolução é o edge propagar `X-Forwarded-For` e a API habilitar `UseForwardedHeaders` com proxies confiáveis explícitos.
- **Evolução:** OIDC via IdP corporativo; rate limit por identidade (claim `sub`) em vez de IP; políticas por rota no edge.
