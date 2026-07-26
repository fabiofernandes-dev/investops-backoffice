# 8. Falha terminal registrada no esgotamento de retries

Status: Accepted

## Contexto

O worker consome `OperationImported`, processa e, em **falha transitória** (banco fora, timeout), reenfileira com backoff; ao esgotar as tentativas, roteia para a DLQ. Até aqui, ele gravava `processing_events` só para os resultados de negócio (`OperationProcessed`/`OperationRejected`, sempre com `attempt_count = 0` e sem erro) e nada ao rotear para a DLQ.

Consequência: a operação ficava presa em `Imported` após o esgotamento (sem status terminal) e sem rastro do erro — as colunas **Tentativas** e **Erro** do Processing Monitor ficavam vazias — e o botão **Reprocessar** (condicional a `Failed`) nunca ativava.

## Decisão

No **esgotamento de tentativas**, antes de publicar na DLQ, o worker registra uma **falha terminal** numa única transação:

1. Marca a operação `Failed` via `UPDATE ... WHERE id=@id AND status NOT IN ('Processed','Rejected')` — o guard no SQL evita sobrescrever um resultado de negócio já gravado; um `Failed` prévio pode ser regravado (idempotente). **Não** seta `rejection_reason` (semântica das rejeições de negócio); o motivo vai em `processing_events.error_message`.
2. Grava um `processing_event` `OperationFailed` com `attempt_count` e `error_message` reais.

O mecanismo é um hook opcional `onExhausted` no `Consumer`, implementado em `operations` (`FailureRecorder.MarkExhausted`); sem o hook, o consumer age como antes. Erros do hook são logados e não bloqueiam o roteamento à DLQ. Só **falhas transitórias** entram no ciclo retry → DLQ → `Failed`; **rejeições de negócio** seguem `Rejected` imediato, sem retry.

## Consequências

- **Prós:** status terminal observável; Processing Monitor mostra tentativas e erro reais; **Reprocessar** passa a ativar (o `Failed` republicado é reprocessado normalmente).
- **Contras:** o `OperationFailed` não é publicado no Outbox — não há consumidor hoje. Fica como evolução.
- **Limitação conhecida:** se o `publishToDlq` falhar **depois** do hook, a mensagem re-esgota; o `UPDATE` é idempotente, mas o insert do `processing_event` não — uma duplicata rara é possível.
- **Evolução:** publicar `OperationFailed` quando houver consumidor; deduplicar por `(operation_id, event_type, attempt_count)` se a duplicata incomodar.
