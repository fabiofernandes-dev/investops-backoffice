# 4. Go no worker de processamento

Status: Accepted

## Contexto

O worker consome eventos do RabbitMQ e executa processamento intensivo e concorrente (carregar dados, aplicar regras, atualizar posição). É um serviço de natureza diferente da API: orientado a throughput, sem UI, com muitos consumidores em paralelo.

## Decisão

Implementar o worker em **Go**, usando `pgx` para PostgreSQL e logs estruturados (`slog`). Mantê-lo simples e explícito, sem frameworks pesados.

## Consequências

- **Prós:** concorrência idiomática (goroutines) ideal para consumir filas; binário único e leve (deploy/containers enxutos); startup rápido; ótimo para um serviço focado em throughput.
- **Contras:** segunda linguagem/stack no projeto (curva e duplicação de alguns contratos); contratos de evento precisam ser honrados nos dois lados — ver [contracts/](../../contracts/) e [0002](0002-use-rabbitmq-for-async-processing.md).
- **Decisão de estudo:** demonstrar poliglotismo e a separação API (negócio/IO web) vs worker (processamento), comum em plataformas de backoffice financeiro.
