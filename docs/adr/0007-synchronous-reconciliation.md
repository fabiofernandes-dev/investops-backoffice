# 7. Conciliação síncrona na API (fora do worker)

Status: Accepted

## Contexto

A conciliação compara a **posição interna** (`portfolio_positions`) com o **extrato do custodiante** (importado via CSV) e registra as **divergências** por `(conta, produto)`. As tabelas `reconciliation_imports` e `reconciliation_divergences` já existiam no schema desde a Fase 1.

O import de operações do sistema é **assíncrono** (Outbox → RabbitMQ → Worker Go), então uma opção natural seria rodar a conciliação pelo mesmo caminho: publicar um evento e deixar o worker calcular. Mas a conciliação é, no fundo, uma **leitura da posição atual** + gravação das divergências — não um processamento com regras de negócio densas como o do worker.

## Decisão

A conciliação é **síncrona na API** (.NET), numa **única transação**: a controller faz o parse do CSV, o handler valida referências, compara cada linha do extrato com a posição interna atual (`ReconciliationComparer`, função pura, quantity-only) e grava `reconciliation_import` + divergências + `AuditLog` atomicamente. **Não** há evento, fila ou consumer novos; o worker e o Outbox ficam intactos.

`resolve`/`ignore` são **transições de estado** (`Pending → Resolved`/`Ignored`) + auditoria e **não** alteram `portfolio_positions` — o worker continua sendo a **fonte da verdade** da posição; ajustar a posição por fora dele reintroduziria dual-write.

## Consequências

- **Prós:** simplicidade (sem contrato de evento/fila/consumer para manter); resultado **imediato** (o import retorna o número de divergências na hora); atomicidade estado + auditoria; comparador puro e fácil de testar.
- **Contras:** um extrato muito grande bloquearia a request — contido pelo limite de `1 MB` no endpoint de import; comparação **quantity-only** (é o que o schema guarda) e restrita aos pares presentes no extrato.
- **Limitação conhecida:** posições internas que não aparecem no extrato não geram divergência (o extrato pode ser parcial).
- **Evolução:** caminho assíncrono via worker para extratos muito grandes; ajuste manual de posição no `resolve` (a "Resolução de divergência"/"Ajuste manual" da SPEC Regra 6); comparação por preço/valor; extrato por `account_number`/`ticker` em vez de UUID.
