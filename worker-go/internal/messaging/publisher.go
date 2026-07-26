package messaging

import (
	"context"
	"fmt"
	"log/slog"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgxpool"
	amqp "github.com/rabbitmq/amqp091-go"
)

type PublisherConfig struct {
	URL        string
	Exchange   string
	PollPeriod time.Duration
	BatchSize  int
}

// amqpChannel é a superfície do canal usada pelo publisher; permite injetar um fake nos testes.
type amqpChannel interface {
	PublishWithContext(ctx context.Context, exchange, key string, mandatory, immediate bool, msg amqp.Publishing) error
	Close() error
}

// Publisher é o relay de outbox do worker: publica os eventos source='worker' no RabbitMQ.
// A conexão é preguiçosa e tolerante a quedas: (re)conecta sob demanda e, ao falhar um envio,
// descarta o canal para reconectar no ciclo seguinte — simétrico ao supervisor do consumidor.
// Cada serviço publica só os seus eventos; SKIP LOCKED / multi-instância: Fase 5.
type Publisher struct {
	pool     *pgxpool.Pool
	connect  func() (amqpChannel, error)
	exchange string
	period   time.Duration
	batch    int
	logger   *slog.Logger
	ch       amqpChannel
}

func NewPublisher(pool *pgxpool.Pool, cfg PublisherConfig, logger *slog.Logger) *Publisher {
	return &Publisher{
		pool:     pool,
		connect:  func() (amqpChannel, error) { return dialBroker(cfg.URL, cfg.Exchange) },
		exchange: cfg.Exchange,
		period:   cfg.PollPeriod,
		batch:    cfg.BatchSize,
		logger:   logger,
	}
}

// dialBroker abre conexão + canal e declara o exchange; o Close do resultado fecha ambos.
func dialBroker(url, exchange string) (amqpChannel, error) {
	conn, err := amqp.Dial(url)
	if err != nil {
		return nil, fmt.Errorf("dial rabbitmq: %w", err)
	}
	ch, err := conn.Channel()
	if err != nil {
		_ = conn.Close()
		return nil, fmt.Errorf("open channel: %w", err)
	}
	if err := ch.ExchangeDeclare(exchange, amqp.ExchangeTopic, true, false, false, false, nil); err != nil {
		_ = conn.Close()
		return nil, fmt.Errorf("declare exchange: %w", err)
	}
	return &brokerChannel{conn: conn, ch: ch}, nil
}

type brokerChannel struct {
	conn *amqp.Connection
	ch   *amqp.Channel
}

func (b *brokerChannel) PublishWithContext(ctx context.Context, exchange, key string, mandatory, immediate bool, msg amqp.Publishing) error {
	return b.ch.PublishWithContext(ctx, exchange, key, mandatory, immediate, msg)
}

func (b *brokerChannel) Close() error {
	if b.ch != nil {
		_ = b.ch.Close()
	}
	if b.conn != nil {
		return b.conn.Close()
	}
	return nil
}

// Run é o único dono do canal: fecha-o ao sair, para que o shutdown não corra com o Close.
func (p *Publisher) Run(ctx context.Context) error {
	defer p.resetChannel()
	ticker := time.NewTicker(p.period)
	defer ticker.Stop()
	for {
		select {
		case <-ctx.Done():
			return ctx.Err()
		case <-ticker.C:
			if _, err := p.PublishPending(ctx); err != nil {
				p.logger.Error("falha no ciclo do relay", "error", err)
			}
		}
	}
}

type pendingRow struct {
	id        uuid.UUID
	eventType string
	payload   string
}

func (p *Publisher) PublishPending(ctx context.Context) (int, error) {
	pending, err := p.queryPending(ctx)
	if err != nil {
		return 0, err
	}
	if len(pending) == 0 {
		return 0, nil
	}

	published, failed, pubErr := p.publishBatch(ctx, pending)
	for _, id := range published {
		if err := p.markPublished(ctx, id); err != nil {
			return len(published), err
		}
	}
	if failed != nil {
		p.markFailed(ctx, failed.id, pubErr)
		p.logger.Warn("falha ao publicar outbox; canal reiniciado", "outboxId", failed.id, "error", pubErr)
		return len(published), pubErr
	}
	return len(published), nil
}

func (p *Publisher) queryPending(ctx context.Context) ([]pendingRow, error) {
	const q = `
		SELECT id, event_type, payload
		FROM outbox_messages
		WHERE status = 'Pending' AND source = 'worker'
		ORDER BY created_at
		LIMIT $1;`
	rows, err := p.pool.Query(ctx, q, p.batch)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var pending []pendingRow
	for rows.Next() {
		var r pendingRow
		if err := rows.Scan(&r.id, &r.eventType, &r.payload); err != nil {
			return nil, err
		}
		pending = append(pending, r)
	}
	return pending, rows.Err()
}

// publishBatch publica as linhas sobre o canal atual, (re)conectando sob demanda. É livre de
// banco. Se um envio falha (canal caído), descarta o canal para reconectar no ciclo seguinte e
// interrompe o lote — as linhas restantes seguem Pending e são retentadas.
func (p *Publisher) publishBatch(ctx context.Context, rows []pendingRow) (published []uuid.UUID, failed *pendingRow, err error) {
	if err := p.ensureChannel(); err != nil {
		return nil, nil, err
	}
	for i := range rows {
		r := rows[i]
		perr := p.ch.PublishWithContext(ctx, p.exchange, r.eventType, false, false, amqp.Publishing{
			ContentType:  "application/json",
			DeliveryMode: amqp.Persistent,
			Body:         []byte(r.payload),
		})
		if perr != nil {
			p.resetChannel()
			return published, &r, perr
		}
		published = append(published, r.id)
	}
	return published, nil, nil
}

func (p *Publisher) ensureChannel() error {
	if p.ch != nil {
		return nil
	}
	ch, err := p.connect()
	if err != nil {
		return err
	}
	p.ch = ch
	return nil
}

func (p *Publisher) resetChannel() {
	if p.ch != nil {
		_ = p.ch.Close()
		p.ch = nil
	}
}

func (p *Publisher) markPublished(ctx context.Context, id uuid.UUID) error {
	_, err := p.pool.Exec(ctx, `UPDATE outbox_messages SET status='Published', published_at=now() WHERE id=$1;`, id)
	return err
}

func (p *Publisher) markFailed(ctx context.Context, id uuid.UUID, cause error) {
	_, _ = p.pool.Exec(ctx, `UPDATE outbox_messages SET attempts=attempts+1, last_error=$2 WHERE id=$1;`, id, cause.Error())
}

func (p *Publisher) Close() error {
	if p.ch != nil {
		err := p.ch.Close()
		p.ch = nil
		return err
	}
	return nil
}
