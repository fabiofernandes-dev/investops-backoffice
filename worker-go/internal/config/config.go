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
	RetryMaxAttempts int
	RetryBackoff     time.Duration
	ReconnectDelay   time.Duration
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
		RetryMaxAttempts: positiveInt(envInt("RETRY_MAX_ATTEMPTS", 3), 3),
		RetryBackoff:     time.Duration(positiveInt(envInt("RETRY_BACKOFF_MS", 5000), 5000)) * time.Millisecond,
		ReconnectDelay:   time.Duration(positiveInt(envInt("RABBITMQ_RECONNECT_DELAY_MS", 5000), 5000)) * time.Millisecond,
	}
}

func positiveInt(value, def int) int {
	if value <= 0 {
		return def
	}
	return value
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
