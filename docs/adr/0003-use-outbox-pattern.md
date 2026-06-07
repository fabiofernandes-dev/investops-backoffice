# 3. Outbox Pattern

Status: Accepted

## Contexto

Ao importar uma operação, precisamos **persistir a operação** e **publicar um evento** no RabbitMQ. Fazer as duas coisas separadamente abre uma janela de inconsistência: se o banco commita e a publicação falha (ou vice-versa), o sistema fica num estado divergente — operação salva sem evento, ou evento sem operação.

## Decisão

Usar o **Outbox Pattern**: na **mesma transação** que grava a operação, gravamos uma linha em `outbox_messages`. Um publisher separado lê as mensagens pendentes e publica no RabbitMQ, marcando-as como `Published`. Assim, persistência e "intenção de publicar" são atômicas.

## Consequências

- **Prós:** atomicidade entre estado e evento (sem dual-write); entrega *at-least-once* garantida; rastreabilidade (status/tentativas/erro na tabela).
- **Contras:** publicação assíncrona (latência até o evento sair); consumidores precisam ser **idempotentes** (a mesma mensagem pode chegar mais de uma vez); exige um processo publisher.
- **Relacionado:** idempotência por `ExternalId` único (SPEC Regra 3); índice `idx_outbox_messages_status_created_at` para varrer pendentes eficientemente.
