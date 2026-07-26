package messaging_test

import (
	"context"
	"path/filepath"
	"runtime"
	"testing"
	"time"

	"investops/worker/internal/database"
	"investops/worker/internal/messaging"
	"investops/worker/internal/observability"

	"github.com/google/uuid"
	amqp "github.com/rabbitmq/amqp091-go"
	"github.com/stretchr/testify/require"
	"github.com/testcontainers/testcontainers-go"
	"github.com/testcontainers/testcontainers-go/modules/postgres"
	"github.com/testcontainers/testcontainers-go/modules/rabbitmq"
)

func schemaScripts(t *testing.T) []string {
	t.Helper()
	_, file, _, _ := runtime.Caller(0)
	dir := filepath.Join(filepath.Dir(file), "..", "..", "..", "backend", "src", "InvestOps.Migrator", "Scripts", "Schema")
	return []string{
		filepath.Join(dir, "Script0001_initial_schema.sql"),
		filepath.Join(dir, "Script0002_outbox_source_and_position_unique.sql"),
	}
}

func TestPublisher_PublishesOnlyWorkerOutbox(t *testing.T) {
	ctx := context.Background()

	pg, err := postgres.Run(ctx, "postgres:16-alpine",
		postgres.WithDatabase("investops"),
		postgres.WithUsername("investops"),
		postgres.WithPassword("investops"),
		postgres.WithInitScripts(schemaScripts(t)...),
		postgres.BasicWaitStrategies(),
	)
	require.NoError(t, err)
	t.Cleanup(func() { _ = testcontainers.TerminateContainer(pg) })

	connStr, err := pg.ConnectionString(ctx, "sslmode=disable")
	require.NoError(t, err)
	pool, err := database.NewPool(ctx, connStr)
	require.NoError(t, err)
	t.Cleanup(pool.Close)

	mq, err := rabbitmq.Run(ctx, "rabbitmq:3.13-management-alpine")
	require.NoError(t, err)
	t.Cleanup(func() { _ = testcontainers.TerminateContainer(mq) })
	amqpURL, err := mq.AmqpURL(ctx)
	require.NoError(t, err)

	// fila/binding antes de publicar
	conn, err := amqp.Dial(amqpURL)
	require.NoError(t, err)
	defer conn.Close()
	ch, err := conn.Channel()
	require.NoError(t, err)
	defer ch.Close()
	require.NoError(t, ch.ExchangeDeclare("investops", amqp.ExchangeTopic, true, false, false, false, nil))
	queue, err := ch.QueueDeclare("", false, true, true, false, nil)
	require.NoError(t, err)
	require.NoError(t, ch.QueueBind(queue.Name, "OperationProcessed", "investops", false, nil))

	workerID := uuid.New()
	_, err = pool.Exec(ctx,
		`INSERT INTO outbox_messages (id,event_type,version,payload,status,attempts,created_at,source) VALUES ($1,'OperationProcessed',1,$2::jsonb,'Pending',0,now(),'worker');`,
		workerID, `{"hello":"worker"}`)
	require.NoError(t, err)
	apiID := uuid.New()
	_, err = pool.Exec(ctx,
		`INSERT INTO outbox_messages (id,event_type,version,payload,status,attempts,created_at,source) VALUES ($1,'OperationImported',1,$2::jsonb,'Pending',0,now(),'api');`,
		apiID, `{"hello":"api"}`)
	require.NoError(t, err)

	publisher := messaging.NewPublisher(pool, messaging.PublisherConfig{
		URL: amqpURL, Exchange: "investops", PollPeriod: time.Second, BatchSize: 50,
	}, observability.New())
	t.Cleanup(func() { _ = publisher.Close() })

	count, err := publisher.PublishPending(ctx)
	require.NoError(t, err)
	require.Equal(t, 1, count, "deve publicar apenas a mensagem source='worker'")

	var body []byte
	for i := 0; i < 25 && body == nil; i++ {
		d, ok, getErr := ch.Get(queue.Name, true)
		require.NoError(t, getErr)
		if ok {
			body = d.Body
		} else {
			time.Sleep(100 * time.Millisecond)
		}
	}
	require.NotNil(t, body)
	require.JSONEq(t, `{"hello":"worker"}`, string(body))

	var workerStatus, apiStatus string
	require.NoError(t, pool.QueryRow(ctx, `SELECT status FROM outbox_messages WHERE id=$1;`, workerID).Scan(&workerStatus))
	require.NoError(t, pool.QueryRow(ctx, `SELECT status FROM outbox_messages WHERE id=$1;`, apiID).Scan(&apiStatus))
	require.Equal(t, "Published", workerStatus)
	require.Equal(t, "Pending", apiStatus, "relay do worker não toca em source='api'")
}
