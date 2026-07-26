# 6. Autenticação fora do escopo do MVP; rate limiting nativo na API

Status: Accepted

## Contexto

O InvestOps é um backoffice interno: em produção ficaria atrás de rede privada, com identidade de um IdP corporativo (OIDC/SAML), nunca com user store próprio. O [SPEC.md](../../SPEC.md) determina "não implementar autenticação complexa no MVP". Ainda assim, a API expõe escrita com efeito colateral (import, reprocess) e um stream SSE, sem proteção contra abuso.

## Decisão

1. **Auth fica fora do MVP** — registrada aqui, não implementada: login JWT com usuários seedados só adicionaria atrito à demo sem demonstrar mais que boilerplate. O design acomoda a evolução (`AddAuthentication` + `[Authorize]` + policies por perfil, mapeadas de claims do IdP).
2. **Rate limiting entra no MVP**, com o middleware nativo do ASP.NET Core 8, sem dependência externa:
   - **Global** (fixed window, ~100 req/min), particionado por IP.
   - **Política `writes`** mais rígida (~20 req/min) em `POST /api/operations/import` e `.../reprocess`.
   - **Health checks isentos.**
   - Configurável por ambiente (seção `RateLimiting`); rejeição `429` + `Retry-After`.

## Consequências

- **Prós:** demo sem atrito; superfície de abuso da escrita contida; a distinção leitura × escrita documenta o risco por endpoint.
- **Contras:** API aberta — qualquer cliente na rede lê e importa. Aceitável só por ser deploy local/demo.
- **Limitação conhecida:** atrás do edge nginx as requisições chegam com o IP do proxy, então o particionamento por IP vira um bucket único (limite agregado — conservador, não inseguro).
- **Evolução:** OIDC via IdP; rate limit por identidade (`sub`) em vez de IP; propagar `X-Forwarded-For` + `UseForwardedHeaders` com proxies confiáveis.
