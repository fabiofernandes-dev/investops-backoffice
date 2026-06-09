package messaging_test

import (
	"context"
	"testing"
	"time"

	"investops/worker/internal/messaging"
	"investops/worker/internal/observability"

	amqp "github.com/rabbitmq/amqp091-go"
	"github.com/stretchr/testify/require"
	"github.com/testcontainers/testcontainers-go"
	"github.com/testcontainers/testcontainers-go/modules/rabbitmq"
)

func TestConsumerReceivesMessage(t *testing.T) {
	ctx := context.Background()

	container, err := rabbitmq.Run(ctx, "rabbitmq:3.13-management-alpine")
	require.NoError(t, err)
	t.Cleanup(func() { _ = testcontainers.TerminateContainer(container) })

	amqpURL, err := container.AmqpURL(ctx)
	require.NoError(t, err)

	cfg := messaging.ConsumerConfig{
		URL:        amqpURL,
		Exchange:   "investops",
		Queue:      "operations.processing",
		RoutingKey: "OperationImported",
	}
	consumer, err := messaging.NewConsumer(cfg, observability.New())
	require.NoError(t, err)
	t.Cleanup(func() { _ = consumer.Close() })

	publishMessage(t, amqpURL, cfg.Exchange, cfg.RoutingKey, []byte(`{"hello":"world"}`))

	received := make(chan []byte, 1)
	consumeCtx, cancel := context.WithCancel(ctx)
	t.Cleanup(cancel)
	go func() {
		_ = consumer.Consume(consumeCtx, func(_ context.Context, body []byte) error {
			received <- body
			return nil
		})
	}()

	select {
	case body := <-received:
		require.JSONEq(t, `{"hello":"world"}`, string(body))
	case <-time.After(15 * time.Second):
		t.Fatal("timeout aguardando mensagem")
	}
}

func publishMessage(t *testing.T, url, exchange, routingKey string, body []byte) {
	t.Helper()
	conn, err := amqp.Dial(url)
	require.NoError(t, err)
	defer conn.Close()

	ch, err := conn.Channel()
	require.NoError(t, err)
	defer ch.Close()

	require.NoError(t, ch.ExchangeDeclare(exchange, amqp.ExchangeTopic, true, false, false, false, nil))
	require.NoError(t, ch.PublishWithContext(context.Background(), exchange, routingKey, false, false, amqp.Publishing{
		ContentType: "application/json",
		Body:        body,
	}))
}
