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

type Handler func(ctx context.Context, body []byte) error

type ConsumerConfig struct {
	URL        string
	Exchange   string
	Queue      string
	RoutingKey string
}

type Consumer struct {
	conn   *amqp.Connection
	ch     *amqp.Channel
	queue  string
	logger *slog.Logger
}

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

	if err := ch.ExchangeDeclare(cfg.Exchange, amqp.ExchangeTopic, true, false, false, false, nil); err != nil {
		_ = conn.Close()
		return nil, fmt.Errorf("declare exchange: %w", err)
	}
	if _, err := ch.QueueDeclare(cfg.Queue, true, false, false, false, nil); err != nil {
		_ = conn.Close()
		return nil, fmt.Errorf("declare queue: %w", err)
	}
	if err := ch.QueueBind(cfg.Queue, cfg.RoutingKey, cfg.Exchange, false, nil); err != nil {
		_ = conn.Close()
		return nil, fmt.Errorf("bind queue: %w", err)
	}
	if err := ch.Qos(1, 0, false); err != nil {
		_ = conn.Close()
		return nil, fmt.Errorf("set qos: %w", err)
	}

	return &Consumer{conn: conn, ch: ch, queue: cfg.Queue, logger: logger}, nil
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
				// shutdown em andamento → encerramento gracioso
				if ctx.Err() != nil {
					return ctx.Err()
				}
				// fechamento inesperado (RabbitMQ caiu/reiniciou): retorna erro para
				// o processo sair != 0 e o orquestrador reiniciar (reconnect in-process: Fase 5).
				return errors.New("canal de entregas fechado inesperadamente")
			}
			c.handle(ctx, handler, delivery)
		}
	}
}

func (c *Consumer) handle(ctx context.Context, handler Handler, delivery amqp.Delivery) {
	// O handler recebe um contexto que sobrevive ao shutdown (com timeout), para a
	// mensagem em voo terminar graciosamente em vez de ser cancelada por um SIGTERM.
	msgCtx, cancel := context.WithTimeout(context.WithoutCancel(ctx), handlerTimeout)
	defer cancel()

	if err := handler(msgCtx, delivery.Body); err != nil {
		c.logger.Error("falha ao processar mensagem", "error", err)
		if nackErr := delivery.Nack(false, true); nackErr != nil {
			c.logger.Error("falha ao nack", "error", nackErr)
		}
		return
	}
	if ackErr := delivery.Ack(false); ackErr != nil {
		c.logger.Error("falha ao ack (mensagem pode ser reentregue)", "error", ackErr)
	}
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
