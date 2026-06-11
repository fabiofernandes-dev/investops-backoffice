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

// Publisher é o relay de outbox do worker: publica os eventos source='worker' no RabbitMQ.
// Cada serviço publica só os seus eventos (independência); SKIP LOCKED / multi-instância: Fase 5.
type Publisher struct {
	pool     *pgxpool.Pool
	conn     *amqp.Connection
	ch       *amqp.Channel
	exchange string
	period   time.Duration
	batch    int
	logger   *slog.Logger
}

func NewPublisher(pool *pgxpool.Pool, cfg PublisherConfig, logger *slog.Logger) (*Publisher, error) {
	conn, err := amqp.Dial(cfg.URL)
	if err != nil {
		return nil, fmt.Errorf("dial rabbitmq: %w", err)
	}
	ch, err := conn.Channel()
	if err != nil {
		_ = conn.Close()
		return nil, fmt.Errorf("open channel: %w", err)
	}
	if err := ch.ExchangeDeclare(cfg.Exchange, amqp.ExchangeTopic, true, false, false, false, nil); err != nil {
		_ = conn.Close()
		return nil, fmt.Errorf("declare exchange: %w", err)
	}
	return &Publisher{
		pool: pool, conn: conn, ch: ch, exchange: cfg.Exchange,
		period: cfg.PollPeriod, batch: cfg.BatchSize, logger: logger,
	}, nil
}

func (p *Publisher) Run(ctx context.Context) error {
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
	const q = `
		SELECT id, event_type, payload
		FROM outbox_messages
		WHERE status = 'Pending' AND source = 'worker'
		ORDER BY created_at
		LIMIT $1;`
	rows, err := p.pool.Query(ctx, q, p.batch)
	if err != nil {
		return 0, err
	}
	var pending []pendingRow
	for rows.Next() {
		var r pendingRow
		if err := rows.Scan(&r.id, &r.eventType, &r.payload); err != nil {
			rows.Close()
			return 0, err
		}
		pending = append(pending, r)
	}
	rows.Close()
	if err := rows.Err(); err != nil {
		return 0, err
	}

	published := 0
	for _, r := range pending {
		if err := p.publish(ctx, r.eventType, r.payload); err != nil {
			p.markFailed(ctx, r.id, err)
			p.logger.Warn("falha ao publicar outbox", "outboxId", r.id, "error", err)
			continue
		}
		if err := p.markPublished(ctx, r.id); err != nil {
			return published, err
		}
		published++
	}
	return published, nil
}

func (p *Publisher) publish(ctx context.Context, eventType, payload string) error {
	return p.ch.PublishWithContext(ctx, p.exchange, eventType, false, false, amqp.Publishing{
		ContentType:  "application/json",
		DeliveryMode: amqp.Persistent,
		Body:         []byte(payload),
	})
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
		_ = p.ch.Close()
	}
	if p.conn != nil {
		return p.conn.Close()
	}
	return nil
}
