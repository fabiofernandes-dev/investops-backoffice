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

// TestConsumerReconnectsAfterConnectionDrop prova que o supervisor sobrevive à queda
// do canal: consome uma mensagem, força a queda apagando a fila (cancela o consumidor
// e fecha o canal de entregas), e verifica que o worker reconecta, redeclara a
// topologia e volta a processar — em vez de encerrar.
func TestConsumerReconnectsAfterConnectionDrop(t *testing.T) {
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

	received := make(chan string, 16)
	handler := func(_ context.Context, body []byte) error {
		received <- string(body)
		return nil
	}

	runCtx, cancel := context.WithCancel(ctx)
	t.Cleanup(cancel)
	go func() {
		_ = messaging.RunConsumer(runCtx, cfg, observability.New(), nil, handler, 200*time.Millisecond)
	}()

	// republica até a fila (declarada de forma assíncrona pelo supervisor) receber a mensagem.
	requireProcessed(t, received, func() {
		publishMessage(t, amqpURL, cfg.Exchange, cfg.RoutingKey, []byte(`{"n":1}`))
	}, `{"n":1}`, "worker deveria consumir a primeira mensagem")

	// força a queda: apagar a fila cancela o consumidor e fecha o canal de entregas,
	// exatamente o cenário que hoje derruba o worker.
	deleteQueue(t, amqpURL, cfg.Queue)

	// o supervisor deve reconectar, redeclarar a topologia e voltar a consumir.
	requireProcessed(t, received, func() {
		publishMessage(t, amqpURL, cfg.Exchange, cfg.RoutingKey, []byte(`{"n":2}`))
	}, `{"n":2}`, "worker deveria reconectar e processar a segunda mensagem")
}

// requireProcessed republica via publish e espera até received entregar want, tolerando
// a corrida entre publicação e a (re)declaração assíncrona da topologia pelo supervisor.
func requireProcessed(t *testing.T, received <-chan string, publish func(), want, msg string) {
	t.Helper()
	require.Eventually(t, func() bool {
		publish()
		select {
		case body := <-received:
			return body == want
		case <-time.After(300 * time.Millisecond):
			return false
		}
	}, 30*time.Second, 500*time.Millisecond, msg)
}

func deleteQueue(t *testing.T, url, queue string) {
	t.Helper()
	conn, err := amqp.Dial(url)
	require.NoError(t, err)
	defer conn.Close()

	ch, err := conn.Channel()
	require.NoError(t, err)
	defer ch.Close()

	_, err = ch.QueueDelete(queue, false, false, false)
	require.NoError(t, err)
}
