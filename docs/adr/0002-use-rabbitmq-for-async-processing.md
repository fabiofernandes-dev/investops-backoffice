# 2. RabbitMQ para processamento assíncrono

Status: Accepted

## Contexto

A importação de uma operação (resposta rápida ao usuário) é uma responsabilidade diferente do processamento (aplicar regras, recalcular posição). Acoplar as duas de forma síncrona deixaria a API lenta e frágil a picos de carga, além de misturar responsabilidades.

## Decisão

Usar **RabbitMQ** como broker de mensagens entre a API (produtor) e o worker (consumidor). A API publica eventos (ex.: `OperationImported`) e o worker consome e processa de forma assíncrona.

## Consequências

- **Prós:** desacoplamento temporal (API responde rápido); absorção de picos; resiliência (retry, e futura DLQ na Fase 5); escalabilidade independente do worker.
- **Contras:** consistência eventual (o status muda depois do processamento); infraestrutura adicional; necessidade de idempotência no consumidor — ver [0003](0003-use-outbox-pattern.md).
- **Alternativas descartadas:** processamento síncrono no request (acopla e bloqueia); fila em tabela com polling puro (mais simples, mas reinventa o broker).
