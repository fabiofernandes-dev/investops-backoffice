# 5. PostgreSQL compartilhado entre API e Worker no MVP

Status: Accepted

## Contexto

API (.NET) e worker (Go) precisam de dados das mesmas entidades (operações, posições, produtos). Em arquitetura "database per service" cada um teria seu banco, mas isso exigiria sincronização de dados e duplicação de schema antes do domínio estar maduro.

## Decisão

Para o MVP, **API e worker compartilham o mesmo PostgreSQL**. A separação se dá por **responsabilidade de serviço, contratos de evento e organização de código** — não por banco. As migrations (DbUp) são a fonte única do schema.

## Consequências

- **Prós:** simplicidade (um schema, uma fonte de verdade); transações fortes onde necessário; menos infraestrutura no MVP.
- **Contras:** acoplamento no nível de dados (mudança de schema afeta os dois serviços); fronteiras dependem de disciplina, não de isolamento físico.
- **Riscos mitigados:** acesso por responsabilidade (a API escreve operações/outbox; o worker atualiza status/posições); idempotência por `ExternalId` — ver [0003](0003-use-outbox-pattern.md).
- **Evolução:** em uma fase futura, cada serviço poderia ter seu próprio banco, com sincronização por eventos.
