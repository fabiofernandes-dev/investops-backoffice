# 7. Conciliação síncrona na API (fora do worker)

Status: Accepted

## Contexto

A conciliação compara a **posição interna** (`portfolio_positions`) com o **extrato do custodiante** (CSV) e registra **divergências** por `(conta, produto)`. O import de operações é assíncrono (Outbox → RabbitMQ → Worker), mas a conciliação é, no fundo, uma **leitura da posição atual** + gravação de divergências — não um processamento com regras densas.

## Decisão

A conciliação é **síncrona na API**, em **uma transação**: a controller faz o parse do CSV, o handler valida referências, compara cada linha com a posição atual (`ReconciliationComparer`, função pura, quantity-only) e grava `reconciliation_import` + divergências + `AuditLog` atomicamente. Sem evento, fila ou consumer novos.

`resolve`/`ignore` são **transições de estado** (`Pending → Resolved`/`Ignored`) + auditoria e **não** alteram `portfolio_positions` — o worker segue como fonte da verdade da posição; ajustá-la por fora reintroduziria dual-write.

## Consequências

- **Prós:** simplicidade (sem contrato/fila/consumer a manter); resultado imediato (o import retorna as divergências na hora); atomicidade estado + auditoria; comparador puro, fácil de testar.
- **Contras:** extrato muito grande bloquearia a request — contido pelo limite de `1 MB` no endpoint; comparação quantity-only, restrita aos pares presentes no extrato.
- **Limitação conhecida:** posições internas ausentes do extrato não geram divergência (o extrato pode ser parcial).
- **Evolução:** caminho assíncrono para extratos grandes; ajuste manual de posição no `resolve`; comparação por preço/valor; extrato por `account_number`/`ticker` em vez de UUID.
