# 9. Retry ilimitado do Outbox sem dead-letter de mensagem "poison"

Status: Accepted

## Contexto

Há um relay de Outbox nos **dois lados** do sistema: a API .NET publica `OperationImported` (`source='api'`) e o Worker Go publica seus eventos de saída (`source='worker'`). Ambos varrem `outbox_messages WHERE status='Pending'`, publicam no RabbitMQ e marcam `Published`; na falha só incrementam `attempts`/`last_error` e **mantêm `Pending`**, então a linha é re-selecionada no próximo ciclo.

A **reconexão** dos dois publishers está resolvida: a API usa `RabbitMqEventBus` com `AutomaticRecoveryEnabled`, e o publisher do worker (re)conecta preguiçosamente — ao falhar um envio, descarta o canal e re-disca no ciclo seguinte, simétrico ao supervisor do consumidor. Uma queda do broker em runtime é absorvida por ambos.

O que **não** está fechado é o tratamento de **mensagem "poison"**: nenhum dos dois publishers tem **cap de tentativas nem dead-letter**. Uma mensagem que nunca pode ser publicada — payload acima do `frame_max` do broker, ou uma linha com `event_type` que a rota não conhece — re-tentaria **indefinidamente**, sobrescrevendo `last_error` a cada ciclo, sem nunca sair de `Pending`. As colunas `attempts`/`last_error` são gravadas mas não lidas para decisão, e `OutboxStatus.Failed` existe no enum/schema mas nunca é gravado para o outbox.

## Decisão

Aceitar o **retry ilimitado** para o caso poison no **MVP** e **não** implementar cap de tentativas nem dead-letter agora.

Justificativa: uma poison message é improvável neste sistema, porque o payload é JSON validado gerado internamente na mesma transação da operação e o publish é um `basicPublish` fino que o broker aceita em quase todos os casos. Construir cap + parqueamento + inspeção para um cenário improvável é o over-engineering que um MVP maduro evita — registra-se a decisão e a evolução.

As colunas `attempts`/`last_error` e o valor `OutboxStatus.Failed` são deixados de propósito: registram a **intenção** e são o alvo direto da evolução — não são código morto.

## Consequências

- **Prós:** simplicidade; nenhuma mensagem legítima se perde (re-tenta até publicar); ambos os publishers reconectam após queda.
- **Contras / Limitação conhecida:** uma poison message (improvável) re-tenta para sempre, ocupando um slot do batch a cada ciclo e sobrescrevendo `last_error` sem visibilidade operacional. Assimetria consciente com o lado do consumo, que tem DLQ + `Failed` terminal (ADR 0008).
- **Evolução:** **cap de tentativas** → ao exceder, marcar a linha `Failed` (status já previsto) e removê-la do `GetPendingAsync`/`queryPending`, expondo as `Failed` numa tela/alerta para inspeção e reprocesso manual; opcionalmente um dead-letter de outbox. Fecha a simetria com o lado do consumo.
