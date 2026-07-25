# 8. Falha terminal registrada no esgotamento de retries

Status: Accepted

## Contexto

O worker Go consome `OperationImported`, processa a operação e, em **falha transitória** (banco indisponível, timeout), reenfileira com backoff e, ao esgotar as tentativas, roteia a mensagem para a DLQ (Fase 5a). Até aqui, o worker gravava `processing_events` **só** para os resultados de negócio `OperationProcessed`/`OperationRejected` (sempre com `attempt_count = 0` e `error_message` nulo) e **nada** ao rotear para a DLQ.

Duas consequências: a operação ficava presa em `Imported` indefinidamente após o esgotamento (sem status terminal), e não havia rastro observável do porquê da falha — as colunas **Tentativas** e **Erro** do Processing Monitor (SPEC §9) não tinham conteúdo. Além disso, o botão **Reprocessar** da tela de operações é condicional a `Failed`, então nunca ativava para uma operação que só falhou transitoriamente.

## Decisão

No **esgotamento de tentativas** (ramo `attempt >= maxAttempts` do consumer), antes de publicar na DLQ, o worker registra uma **falha terminal**, numa única transação:

1. Marca a **operação `Failed`** — `UPDATE operations SET status='Failed' WHERE id=@id AND status NOT IN ('Processed','Rejected')`. O guard vive no **SQL**: um resultado de negócio já gravado (`Processed`/`Rejected`) nunca é sobrescrito por uma falha tardia; um `Failed` prévio pode ser regravado (idempotente). **Não** seta `rejection_reason` — essa coluna é semântica das **rejeições de negócio**; o motivo da falha transitória fica em `processing_events.error_message`.
2. Grava um `processing_event` **`OperationFailed`** com `attempt_count` e `error_message` **reais** (a causa do último erro), dando conteúdo às colunas Tentativas/Erro do monitor.

O mecanismo é um **hook opcional `onExhausted`** no `Consumer` (camada de mensageria), implementado na camada `operations` (`FailureRecorder.MarkExhausted`) que faz o parse do evento e abre a transação. Sem o hook, o consumer se comporta como antes (protege os testes que criam `NewConsumer` sem callback). Erros do hook são **logados e não bloqueiam** o roteamento à DLQ.

Distinção preservada: **só falhas transitórias** entram no ciclo retry → DLQ → `Failed`; **rejeições de negócio** (perfil incompatível, posição insuficiente) seguem `Rejected` imediato, sem retry — comportamento inalterado.

## Consequências

- **Prós:** a operação ganha um status terminal observável; o Processing Monitor mostra tentativas e erro reais; o **Reprocessar** passa a ativar para falhas transitórias esgotadas (o skip de idempotência do processor cobre só `Processed`/`Rejected`, então um `Failed` republicado é reprocessado normalmente).
- **Contras:** o `OperationFailed` **não** é publicado no Outbox/RabbitMQ — não há consumidor para ele hoje, e publicá-lo só adicionaria um evento morto. Fica como evolução se surgir um interessado (ex.: notificação).
- **Limitação conhecida:** se o `publishToDlq` **falhar depois** do hook já ter rodado, a mensagem é reenfileirada e pode re-esgotar. O `UPDATE` de status é idempotente, mas o `InsertProcessingEvent` não — uma **duplicata rara** do `processing_event` `OperationFailed` é possível. Aceitável pela raridade.
- **Evolução:** publicar `OperationFailed` no Outbox quando houver consumidor; deduplicar o `processing_event` por `(operation_id, event_type, attempt_count)` se a duplicata rara incomodar.
