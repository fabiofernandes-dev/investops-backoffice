package messaging

import (
	"context"
	"log/slog"
	"time"
)

// session é a superfície do consumidor que o supervisor dirige; *Consumer a satisfaz.
type session interface {
	Consume(ctx context.Context, handler Handler) error
	Close() error
}

// RunConsumer supervisiona o consumo tolerando quedas de conexão/canal do RabbitMQ:
// (re)cria o consumidor, aplica setup (ex.: SetOnExhausted), consome e, em erro
// transiente, aguarda reconnectDelay e reconecta indefinidamente. Retorna nil quando
// o contexto é cancelado (SIGINT/SIGTERM), permitindo encerramento limpo.
func RunConsumer(ctx context.Context, cfg ConsumerConfig, logger *slog.Logger, setup func(*Consumer), handler Handler, reconnectDelay time.Duration) error {
	connect := func() (session, error) {
		consumer, err := NewConsumer(cfg, logger)
		if err != nil {
			return nil, err
		}
		if setup != nil {
			setup(consumer)
		}
		return consumer, nil
	}
	return superviseConsumer(ctx, logger, connect, handler, reconnectDelay)
}

func superviseConsumer(ctx context.Context, logger *slog.Logger, connect func() (session, error), handler Handler, reconnectDelay time.Duration) error {
	for {
		if ctx.Err() != nil {
			return nil
		}

		s, err := connect()
		if err != nil {
			logger.Error("falha ao conectar no RabbitMQ; nova tentativa após backoff", "error", err, "delay", reconnectDelay.String())
			if !waitOrCancel(ctx, reconnectDelay) {
				return nil
			}
			continue
		}

		err = s.Consume(ctx, handler)
		_ = s.Close()

		if ctx.Err() != nil {
			return nil
		}
		logger.Warn("conexão com o RabbitMQ perdida; reconectando após backoff", "error", err, "delay", reconnectDelay.String())
		if !waitOrCancel(ctx, reconnectDelay) {
			return nil
		}
	}
}

// waitOrCancel aguarda d ou retorna false se o contexto for cancelado antes.
func waitOrCancel(ctx context.Context, d time.Duration) bool {
	if d <= 0 {
		return ctx.Err() == nil
	}
	timer := time.NewTimer(d)
	defer timer.Stop()
	select {
	case <-ctx.Done():
		return false
	case <-timer.C:
		return true
	}
}
