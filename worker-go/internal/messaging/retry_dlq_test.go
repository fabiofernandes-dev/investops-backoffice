package messaging_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"investops/worker/internal/messaging"
	"investops/worker/internal/observability"

	amqp "github.com/rabbitmq/amqp091-go"
	"github.com/stretchr/testify/require"
	"github.com/testcontainers/testcontainers-go"
	"github.com/testcontainers/testcontainers-go/modules/rabbitmq"
)

func startConsumer(t *testing.T, handler messaging.Handler, hooks ...messaging.OnExhaustedFunc) (string, messaging.ConsumerConfig) {
	t.Helper()
	ctx := context.Background()

	container, err := rabbitmq.Run(ctx, "rabbitmq:3.13-management-alpine")
	require.NoError(t, err)
	t.Cleanup(func() { _ = testcontainers.TerminateContainer(container) })

	amqpURL, err := container.AmqpURL(ctx)
	require.NoError(t, err)

	cfg := messaging.ConsumerConfig{
		URL:         amqpURL,
		Exchange:    "investops",
		Queue:       "operations.processing",
		RoutingKey:  "OperationImported",
		MaxAttempts: 3,
		Backoff:     200 * time.Millisecond,
	}
	consumer, err := messaging.NewConsumer(cfg, observability.New())
	require.NoError(t, err)
	t.Cleanup(func() { _ = consumer.Close() })

	if len(hooks) > 0 {
		consumer.SetOnExhausted(hooks[0])
	}

	consumeCtx, cancel := context.WithCancel(ctx)
	t.Cleanup(cancel)
	go func() { _ = consumer.Consume(consumeCtx, handler) }()

	return amqpURL, cfg
}

func TestPoisonMessageGoesToDlqAfterMaxAttempts(t *testing.T) {
	url, cfg := startConsumer(t, func(_ context.Context, _ []byte) error {
		return errors.New("falha proposital")
	})

	publishMessage(t, url, cfg.Exchange, cfg.RoutingKey, []byte(`{"poison":true}`))

	msg := waitForDlqMessage(t, url, 25*time.Second)
	require.JSONEq(t, `{"poison":true}`, string(msg.Body))
}

func TestOnExhaustedCalledWithMaxAttemptsOnExhaustion(t *testing.T) {
	exhausted := make(chan int, 4)
	hook := func(_ context.Context, _ []byte, attempt int, _ error) {
		exhausted <- attempt
	}
	url, cfg := startConsumer(t, func(_ context.Context, _ []byte) error {
		return errors.New("falha proposital")
	}, hook)

	publishMessage(t, url, cfg.Exchange, cfg.RoutingKey, []byte(`{"poison":true}`))

	select {
	case attempt := <-exhausted:
		require.Equal(t, cfg.MaxAttempts, attempt, "onExhausted deve ser chamado com attempt == maxAttempts")
	case <-time.After(25 * time.Second):
		t.Fatal("timeout aguardando o callback onExhausted")
	}
}

func TestProcessedMessageDoesNotGoToDlq(t *testing.T) {
	url, cfg := startConsumer(t, func(_ context.Context, _ []byte) error {
		return nil // sucesso -> ack
	})

	publishMessage(t, url, cfg.Exchange, cfg.RoutingKey, []byte(`{"ok":true}`))

	// dá tempo de processar; a DLQ deve permanecer vazia.
	time.Sleep(2 * time.Second)
	_, ok := getFromDlq(t, url)
	require.False(t, ok, "mensagem processada com sucesso não deve ir para a DLQ")
}

func waitForDlqMessage(t *testing.T, url string, timeout time.Duration) amqp.Delivery {
	t.Helper()
	deadline := time.After(timeout)
	tick := time.NewTicker(300 * time.Millisecond)
	defer tick.Stop()
	for {
		select {
		case <-deadline:
			t.Fatal("timeout aguardando mensagem na DLQ")
		case <-tick.C:
			if msg, ok := getFromDlq(t, url); ok {
				return msg
			}
		}
	}
}

func getFromDlq(t *testing.T, url string) (amqp.Delivery, bool) {
	t.Helper()
	conn, err := amqp.Dial(url)
	require.NoError(t, err)
	defer conn.Close()
	ch, err := conn.Channel()
	require.NoError(t, err)
	defer ch.Close()

	msg, ok, err := ch.Get(messaging.QueueDlq, true)
	require.NoError(t, err)
	return msg, ok
}
