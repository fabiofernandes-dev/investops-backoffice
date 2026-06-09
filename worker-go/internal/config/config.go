package config

import "os"

type Config struct {
	DatabaseURL string
	RabbitMQURL string
	Exchange    string
	Queue       string
	RoutingKey  string
}

func Load() Config {
	return Config{
		DatabaseURL: env("DATABASE_URL", "postgres://investops:investops@localhost:5432/investops"),
		RabbitMQURL: env("RABBITMQ_URL", "amqp://investops:investops@localhost:5672/"),
		Exchange:    env("RABBITMQ_EXCHANGE", "investops"),
		Queue:       env("RABBITMQ_QUEUE", "operations.processing"),
		RoutingKey:  env("RABBITMQ_ROUTING_KEY", "OperationImported"),
	}
}

func env(key, def string) string {
	if v, ok := os.LookupEnv(key); ok && v != "" {
		return v
	}
	return def
}
