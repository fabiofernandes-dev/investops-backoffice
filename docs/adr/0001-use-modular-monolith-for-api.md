# 1. Modular monolith para a API

Status: Accepted

## Contexto

O sistema tem regras de negócio densas (suitability, posição, preço médio, idempotência) e várias áreas (operações, posições, conciliação, auditoria). Seria possível começar com microservices por área, mas isso traz custo operacional alto (deploy, observabilidade distribuída, consistência) antes de o domínio estar maduro.

## Decisão

A API .NET será um **modular monolith**: um único deployável, organizado internamente em camadas (Domain, Application, Infrastructure, Api) e por módulos de negócio. O processamento assíncrono pesado fica num serviço separado (worker Go), comunicando por eventos — ver [0002](0002-use-rabbitmq-for-async-processing.md) e [0004](0004-use-go-for-processing-worker.md).

## Consequências

- **Prós:** simplicidade de deploy e debug; transações locais; refatoração de fronteiras barata enquanto o domínio evolui; menos infraestrutura.
- **Contras:** escala de forma monolítica; disciplina necessária para não acoplar módulos.
- **Evolução:** módulos com fronteiras claras podem ser extraídos em serviços próprios no futuro, se houver necessidade real de escala/independência.
