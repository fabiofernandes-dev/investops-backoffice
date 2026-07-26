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

	store := database.NewStore(pool)
	processor := operations.NewProcessor(store, logger)
	failureRecorder := operations.NewFailureRecorder(store, logger)

	publisher, err := messaging.NewPublisher(pool, messaging.PublisherConfig{
		URL:        cfg.RabbitMQURL,
		Exchange:   cfg.Exchange,
		PollPeriod: cfg.OutboxPollPeriod,
		BatchSize:  cfg.OutboxBatchSize,
	}, logger)
	if err != nil {
		logger.Error("falha ao iniciar o relay de outbox", "error", err)
		os.Exit(1)
	}
	defer publisher.Close()

	go func() {
		if err := publisher.Run(ctx); err != nil && ctx.Err() == nil {
			logger.Error("relay encerrou com erro", "error", err)
		}
	}()

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

	consumerCfg := messaging.ConsumerConfig{
		URL:         cfg.RabbitMQURL,
		Exchange:    cfg.Exchange,
		Queue:       cfg.Queue,
		RoutingKey:  cfg.RoutingKey,
		MaxAttempts: cfg.RetryMaxAttempts,
		Backoff:     cfg.RetryBackoff,
	}
	setup := func(c *messaging.Consumer) { c.SetOnExhausted(failureRecorder.MarkExhausted) }

	if err := messaging.RunConsumer(ctx, consumerCfg, logger, setup, handler, cfg.ReconnectDelay); err != nil {
		logger.Error("consumer encerrou com erro", "error", err)
		os.Exit(1)
	}

	logger.Info("worker encerrado")
}
