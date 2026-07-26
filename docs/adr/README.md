# Architecture Decision Records

Decisões de arquitetura do InvestOps Backoffice — cada uma com contexto, alternativas descartadas, consequências e, quando aplicável, **limitação conhecida** e **evolução**. São o registro honesto do porquê de cada escolha do MVP.

| # | Decisão | Resumo |
| - | ------- | ------ |
| [0001](0001-use-modular-monolith-for-api.md) | Modular monolith na API | Um deploy .NET com módulos de fronteira clara em vez de microserviços — simplicidade operacional no MVP, sem acoplar times a rede. |
| [0002](0002-use-rabbitmq-for-async-processing.md) | RabbitMQ para processamento assíncrono | Desacopla o import (rápido, síncrono) do processamento de regras (worker), com filas duráveis. |
| [0003](0003-use-outbox-pattern.md) | Outbox Pattern | Persistir operação e evento na **mesma transação**; publisher separado faz o relay — elimina o dual-write. |
| [0004](0004-use-go-for-processing-worker.md) | Go no worker de processamento | Consumidor concorrente enxuto para as regras de negócio; políglota via contratos de evento. |
| [0005](0005-use-postgresql-shared-database-for-mvp.md) | PostgreSQL compartilhado no MVP | Um banco entre API e worker para simplificar; caminho para database-per-service registrado. |
| [0006](0006-defer-authentication-adopt-rate-limiting.md) | Adiar auth, adotar rate limiting | Backoffice interno assume rede privada + IdP corporativo; o design acomoda `[Authorize]` + policies. |
| [0007](0007-synchronous-reconciliation.md) | Conciliação síncrona | Comparação por quantidade no request do import; limitação de extrato parcial documentada. |
| [0008](0008-terminal-failure-on-retry-exhaustion.md) | Falha terminal no esgotamento de retries | Ao esgotar o retry → DLQ, o worker grava `Failed` + tentativas/erro reais (Processing Monitor). |
| [0009](0009-outbox-unbounded-retry-mvp.md) | Retry ilimitado do Outbox | Ambos os publishers reconectam; falta cap/dead-letter de mensagem "poison" — assimetria com o consumer (que tem DLQ) registrada. |
