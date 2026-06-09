package main

import (
	"context"
	"encoding/json"
	"os"
	"os/signal"
	"syscall"

	"investops/worker/internal/config"
	"investops/worker/internal/database"
	"investops/worker/internal/messaging"
	"investops/worker/internal/observability"
	"investops/worker/internal/operations"
)

func main() {
	logger := observability.New()
	cfg := config.Load()

	ctx, stop := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)
	defer stop()

	pool, err := database.NewPool(ctx, cfg.DatabaseURL)
	if err != nil {
		logger.Error("falha ao conectar no Postgres", "error", err)
		os.Exit(1)
	}
	defer pool.Close()

	consumer, err := messaging.NewConsumer(messaging.ConsumerConfig{
		URL:        cfg.RabbitMQURL,
		Exchange:   cfg.Exchange,
		Queue:      cfg.Queue,
		RoutingKey: cfg.RoutingKey,
	}, logger)
	if err != nil {
		logger.Error("falha ao conectar no RabbitMQ", "error", err)
		os.Exit(1)
	}
	defer consumer.Close()

	processor := operations.NewProcessor(database.NewStore(pool), logger)

	logger.Info("worker iniciado", "queue", cfg.Queue, "exchange", cfg.Exchange)

	handler := func(ctx context.Context, body []byte) error {
		var evt operations.OperationImportedEvent
		if err := json.Unmarshal(body, &evt); err != nil {
			// payload malformado: logar e descartar (ack) — requeue não resolveria.
			logger.Error("evento inválido; descartando", "error", err)
			return nil
		}
		return processor.Process(ctx, evt)
	}

	if err := consumer.Consume(ctx, handler); err != nil && ctx.Err() == nil {
		logger.Error("consumer encerrou com erro", "error", err)
		os.Exit(1)
	}

	logger.Info("worker encerrado")
}
