package config

import (
	"os"
	"strconv"
	"time"
)

type Config struct {
	DatabaseURL      string
	RabbitMQURL      string
	Exchange         string
	Queue            string
	RoutingKey       string
	OutboxPollPeriod time.Duration
	OutboxBatchSize  int
}

func Load() Config {
	return Config{
		DatabaseURL:      env("DATABASE_URL", "postgres://investops:investops@localhost:5432/investops"),
		RabbitMQURL:      env("RABBITMQ_URL", "amqp://investops:investops@localhost:5672/"),
		Exchange:         env("RABBITMQ_EXCHANGE", "investops"),
		Queue:            env("RABBITMQ_QUEUE", "operations.processing"),
		RoutingKey:       env("RABBITMQ_ROUTING_KEY", "OperationImported"),
		OutboxPollPeriod: time.Duration(envInt("OUTBOX_POLL_SECONDS", 2)) * time.Second,
		OutboxBatchSize:  envInt("OUTBOX_BATCH_SIZE", 50),
	}
}

func env(key, def string) string {
	if v, ok := os.LookupEnv(key); ok && v != "" {
		return v
	}
	return def
}

func envInt(key string, def int) int {
	if v, ok := os.LookupEnv(key); ok {
		if n, err := strconv.Atoi(v); err == nil {
			return n
		}
	}
	return def
}
