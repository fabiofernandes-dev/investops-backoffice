# Política de Segurança

## Sobre este projeto

InvestOps Backoffice é um **projeto de portfólio / demonstração**. A stack local sobe com **credenciais de demonstração** (`investops/investops`) e sem autenticação, por decisão de escopo documentada no [ADR 0006](docs/adr/0006-defer-authentication-adopt-rate-limiting.md). Isso é **intencional** para facilitar o onboarding e **não** deve ser tratado como vulnerabilidade — a stack não deve ser exposta na internet (ver aviso no [README](README.md)).

## Reportar uma vulnerabilidade

Se encontrar um problema de segurança **real** (não os defaults de demo acima), por favor **não abra uma issue pública**. Use o canal privado do GitHub:

1. Aba **Security** do repositório → **Report a vulnerability** (GitHub Security Advisories).
2. Descreva o problema, o impacto e um passo a passo de reprodução.

Você receberá um retorno assim que possível. Correções relevantes serão publicadas na branch `main`.

## Versões suportadas

Apenas a branch `main` (última release) recebe correções.
