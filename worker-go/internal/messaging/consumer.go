package messaging

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"time"

	amqp "github.com/rabbitmq/amqp091-go"
)

// tempo máximo que uma mensagem em voo tem para terminar após um shutdown.
const handlerTimeout = 30 * time.Second

// Topologia de retry/DLQ (Fase 5a).
const (
	exchangeRetry = "investops.retry"
	exchangeDlq   = "investops.dlq"
	queueRetry    = "operations.retry"
	QueueDlq      = "operations.dlq"
	queueTypeArg  = "x-queue-type"
	quorum        = "quorum"
)

type Handler func(ctx context.Context, body []byte) error

// OnExhaustedFunc é chamado quando uma mensagem esgota as tentativas e vai para a DLQ.
type OnExhaustedFunc func(ctx context.Context, body []byte, attempt int, cause error)

type ConsumerConfig struct {
	URL         string
	Exchange    string
	Queue       string
	RoutingKey  string
	MaxAttempts int
	Backoff     time.Duration
}

type Consumer struct {
	conn        *amqp.Connection
	ch          *amqp.Channel
	queue       string
	maxAttempts int
	logger      *slog.Logger
	onExhausted OnExhaustedFunc
}

// SetOnExhausted registra um callback opcional invocado no esgotamento de tentativas,
// antes do roteamento para a DLQ. Sem callback, o comportamento é o atual.
func (c *Consumer) SetOnExhausted(fn OnExhaustedFunc) { c.onExhausted = fn }

func NewConsumer(cfg ConsumerConfig, logger *slog.Logger) (*Consumer, error) {
	conn, err := amqp.Dial(cfg.URL)
	if err != nil {
		return nil, fmt.Errorf("dial rabbitmq: %w", err)
	}

	ch, err := conn.Channel()
	if err != nil {
		_ = conn.Close()
		return nil, fmt.Errorf("open channel: %w", err)
	}

	if err := declareTopology(ch, cfg); err != nil {
		_ = conn.Close()
		return nil, err
	}
	if err := ch.Qos(1, 0, false); err != nil {
		_ = conn.Close()
		return nil, fmt.Errorf("set qos: %w", err)
	}

	return &Consumer{conn: conn, ch: ch, queue: cfg.Queue, maxAttempts: cfg.MaxAttempts, logger: logger}, nil
}

// declareTopology declara (idempotente) exchanges, filas quorum e bindings do fluxo principal + retry + DLQ.
func declareTopology(ch *amqp.Channel, cfg ConsumerConfig) error {
	for _, ex := range []struct {
		name string
		kind string
	}{
		{cfg.Exchange, amqp.ExchangeTopic},
		{exchangeRetry, amqp.ExchangeFanout},
		{exchangeDlq, amqp.ExchangeFanout},
	} {
		if err := ch.ExchangeDeclare(ex.name, ex.kind, true, false, false, false, nil); err != nil {
			return fmt.Errorf("declare exchange %s: %w", ex.name, err)
		}
	}

	// principal (quorum): em dead-letter vai para o exchange de retry.
	if _, err := ch.QueueDeclare(cfg.Queue, true, false, false, false, amqp.Table{
		queueTypeArg:             quorum,
		"x-dead-letter-exchange": exchangeRetry,
	}); err != nil {
		return fmt.Errorf("declare queue %s: %w", cfg.Queue, err)
	}
	// retry (quorum): segura a mensagem por TTL e devolve à principal (backoff).
	if _, err := ch.QueueDeclare(queueRetry, true, false, false, false, amqp.Table{
		queueTypeArg:                quorum,
		"x-message-ttl":             int32(cfg.Backoff.Milliseconds()),
		"x-dead-letter-exchange":    cfg.Exchange,
		"x-dead-letter-routing-key": cfg.RoutingKey,
	}); err != nil {
		return fmt.Errorf("declare queue %s: %w", queueRetry, err)
	}
	// DLQ (quorum): destino final após esgotar as tentativas.
	if _, err := ch.QueueDeclare(QueueDlq, true, false, false, false, amqp.Table{
		queueTypeArg: quorum,
	}); err != nil {
		return fmt.Errorf("declare queue %s: %w", QueueDlq, err)
	}

	for _, b := range []struct {
		queue    string
		key      string
		exchange string
	}{
		{cfg.Queue, cfg.RoutingKey, cfg.Exchange},
		{queueRetry, "", exchangeRetry},
		{QueueDlq, "", exchangeDlq},
	} {
		if err := ch.QueueBind(b.queue, b.key, b.exchange, false, nil); err != nil {
			return fmt.Errorf("bind queue %s: %w", b.queue, err)
		}
	}
	return nil
}

func (c *Consumer) Consume(ctx context.Context, handler Handler) error {
	deliveries, err := c.ch.Consume(c.queue, "", false, false, false, false, nil)
	if err != nil {
		return fmt.Errorf("consume: %w", err)
	}

	for {
		select {
		case <-ctx.Done():
			return ctx.Err()
		case delivery, ok := <-deliveries:
			if !ok {
				if ctx.Err() != nil {
					return ctx.Err()
				}
				return errors.New("canal de entregas fechado inesperadamente")
			}
			c.handle(ctx, handler, delivery)
		}
	}
}

func (c *Consumer) handle(ctx context.Context, handler Handler, delivery amqp.Delivery) {
	msgCtx, cancel := context.WithTimeout(context.WithoutCancel(ctx), handlerTimeout)
	defer cancel()

	if err := handler(msgCtx, delivery.Body); err != nil {
		attempt := int(deathCount(delivery, c.queue)) + 1
		if attempt >= c.maxAttempts {
			c.logger.Error("esgotou tentativas; enviando para a DLQ", "attempt", attempt, "error", err)
			if c.onExhausted != nil {
				c.onExhausted(ctx, delivery.Body, attempt, err)
			}
			if pubErr := c.publishToDlq(ctx, delivery); pubErr != nil {
				c.logger.Error("falha ao publicar na DLQ; reenfileirando", "error", pubErr)
				_ = delivery.Nack(false, false)
				return
			}
			if ackErr := delivery.Ack(false); ackErr != nil {
				c.logger.Error("falha ao ack após DLQ", "error", ackErr)
			}
			return
		}
		c.logger.Warn("falha ao processar; reenfileirando para retry", "attempt", attempt, "error", err)
		// requeue=false -> dead-letter para o exchange de retry (wait queue com TTL).
		if nackErr := delivery.Nack(false, false); nackErr != nil {
			c.logger.Error("falha ao nack", "error", nackErr)
		}
		return
	}
	if ackErr := delivery.Ack(false); ackErr != nil {
		c.logger.Error("falha ao ack (mensagem pode ser reentregue)", "error", ackErr)
	}
}

func (c *Consumer) publishToDlq(ctx context.Context, delivery amqp.Delivery) error {
	return c.ch.PublishWithContext(context.WithoutCancel(ctx), exchangeDlq, "", false, false, amqp.Publishing{
		ContentType:  delivery.ContentType,
		Body:         delivery.Body,
		Headers:      delivery.Headers,
		DeliveryMode: amqp.Persistent,
	})
}

// deathCount lê do header x-death quantas vezes a mensagem foi rejeitada pela fila principal.
// Filtra por queue+reason (a entrada mais recente pode ser a da fila de retry, reason "expired").
func deathCount(d amqp.Delivery, queue string) int64 {
	raw, ok := d.Headers["x-death"]
	if !ok {
		return 0
	}
	entries, ok := raw.([]interface{})
	if !ok {
		return 0
	}
	for _, e := range entries {
		t, ok := e.(amqp.Table)
		if !ok {
			continue
		}
		if t["queue"] == queue && t["reason"] == "rejected" {
			if count, ok := t["count"].(int64); ok {
				return count
			}
		}
	}
	return 0
}

func (c *Consumer) Close() error {
	if c.ch != nil {
		_ = c.ch.Close()
	}
	if c.conn != nil {
		return c.conn.Close()
	}
	return nil
}
