package database_test

import (
	"context"
	"log/slog"
	"path/filepath"
	"runtime"
	"testing"

	"investops/worker/internal/database"
	"investops/worker/internal/operations"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5/pgxpool"
	"github.com/shopspring/decimal"
	"github.com/stretchr/testify/require"
	"github.com/testcontainers/testcontainers-go"
	"github.com/testcontainers/testcontainers-go/modules/postgres"
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

func newPool(t *testing.T) *pgxpool.Pool {
	t.Helper()
	ctx := context.Background()

	container, err := postgres.Run(ctx, "postgres:16-alpine",
		postgres.WithDatabase("investops"),
		postgres.WithUsername("investops"),
		postgres.WithPassword("investops"),
		postgres.WithInitScripts(schemaScripts(t)...),
		postgres.BasicWaitStrategies(),
	)
	require.NoError(t, err)
	t.Cleanup(func() { _ = testcontainers.TerminateContainer(container) })

	connStr, err := container.ConnectionString(ctx, "sslmode=disable")
	require.NoError(t, err)

	pool, err := database.NewPool(ctx, connStr)
	require.NoError(t, err)
	t.Cleanup(pool.Close)
	return pool
}

func seedReferences(t *testing.T, pool *pgxpool.Pool, profile string, riskLevel int) (accountID, productID uuid.UUID) {
	t.Helper()
	ctx := context.Background()
	customerID := uuid.New()
	accountID = uuid.New()
	productID = uuid.New()

	_, err := pool.Exec(ctx,
		`INSERT INTO customers (id, name, document_number, investor_profile, status) VALUES ($1,'T','0',$2,'Active');`,
		customerID, profile)
	require.NoError(t, err)
	_, err = pool.Exec(ctx,
		`INSERT INTO investment_accounts (id, customer_id, account_number, status) VALUES ($1,$2,'ACC','Active');`,
		accountID, customerID)
	require.NoError(t, err)
	_, err = pool.Exec(ctx,
		`INSERT INTO financial_products (id, product_type, name, risk_level, liquidity_days, is_active) VALUES ($1,'Stock','P',$2,2,true);`,
		productID, riskLevel)
	require.NoError(t, err)
	return accountID, productID
}

func seedOperation(t *testing.T, pool *pgxpool.Pool, accountID, productID uuid.UUID, opType string, qty, price string) uuid.UUID {
	t.Helper()
	id := uuid.New()
	_, err := pool.Exec(context.Background(),
		`INSERT INTO operations
		   (id, external_id, account_id, product_id, operation_type, quantity, unit_price, gross_value, net_value, operation_date, settlement_date, status, created_at, updated_at)
		 VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$8,'2026-06-09','2026-06-11','Imported',now(),now());`,
		id, "EXT-"+id.String(), accountID, productID, opType,
		decimal.RequireFromString(qty), decimal.RequireFromString(price), decimal.RequireFromString(qty).Mul(decimal.RequireFromString(price)))
	require.NoError(t, err)
	return id
}

func event(opID uuid.UUID) operations.OperationImportedEvent {
	return operations.OperationImportedEvent{
		CorrelationID: uuid.New(),
		Payload:       operations.OperationImportedPayload{OperationID: opID},
	}
}

func discard() *slog.Logger { return slog.New(slog.DiscardHandler) }

func TestStore_BuyValid(t *testing.T) {
	ctx := context.Background()
	pool := newPool(t)
	processor := operations.NewProcessor(database.NewStore(pool), discard())

	accountID, productID := seedReferences(t, pool, "Moderate", 3)
	opID := seedOperation(t, pool, accountID, productID, "Buy", "100", "10")

	require.NoError(t, processor.Process(ctx, event(opID)))

	var status string
	require.NoError(t, pool.QueryRow(ctx, `SELECT status FROM operations WHERE id=$1;`, opID).Scan(&status))
	require.Equal(t, "Processed", status)

	var qty, avg decimal.Decimal
	require.NoError(t, pool.QueryRow(ctx,
		`SELECT quantity, average_price FROM portfolio_positions WHERE account_id=$1 AND product_id=$2;`,
		accountID, productID).Scan(&qty, &avg))
	require.True(t, qty.Equal(decimal.RequireFromString("100")))
	require.True(t, avg.Equal(decimal.RequireFromString("10")))

	var processed, positionUpdated int
	require.NoError(t, pool.QueryRow(ctx,
		`SELECT count(*) FROM outbox_messages WHERE source='worker' AND event_type='OperationProcessed';`).Scan(&processed))
	require.NoError(t, pool.QueryRow(ctx,
		`SELECT count(*) FROM outbox_messages WHERE source='worker' AND event_type='PositionUpdated';`).Scan(&positionUpdated))
	require.Equal(t, 1, processed)
	require.Equal(t, 1, positionUpdated)
	// confirma que o payload do OperationProcessed está em camelCase com o operationId correto
	var found int
	require.NoError(t, pool.QueryRow(ctx,
		`SELECT count(*) FROM outbox_messages WHERE event_type='OperationProcessed' AND payload->'payload'->>'operationId' = $1;`,
		opID.String()).Scan(&found))
	require.Equal(t, 1, found)
}

func TestStore_RejectBySuitability(t *testing.T) {
	ctx := context.Background()
	pool := newPool(t)
	processor := operations.NewProcessor(database.NewStore(pool), discard())

	accountID, productID := seedReferences(t, pool, "Conservative", 5)
	opID := seedOperation(t, pool, accountID, productID, "Buy", "10", "5")

	require.NoError(t, processor.Process(ctx, event(opID)))

	var status, reason string
	require.NoError(t, pool.QueryRow(ctx, `SELECT status, rejection_reason FROM operations WHERE id=$1;`, opID).Scan(&status, &reason))
	require.Equal(t, "Rejected", status)
	require.Equal(t, "InvestorProfileNotCompatible", reason)

	var positions int
	require.NoError(t, pool.QueryRow(ctx, `SELECT count(*) FROM portfolio_positions WHERE account_id=$1;`, accountID).Scan(&positions))
	require.Equal(t, 0, positions)
}

func TestStore_MarkOperationFailed_TransitionsImportedToFailed(t *testing.T) {
	ctx := context.Background()
	pool := newPool(t)
	store := database.NewStore(pool)

	accountID, productID := seedReferences(t, pool, "Moderate", 3)
	opID := seedOperation(t, pool, accountID, productID, "Buy", "100", "10")

	require.NoError(t, store.WithinTx(ctx, func(tx operations.TxRepo) error {
		return tx.MarkOperationFailed(ctx, opID)
	}))

	var status string
	require.NoError(t, pool.QueryRow(ctx, `SELECT status FROM operations WHERE id=$1;`, opID).Scan(&status))
	require.Equal(t, "Failed", status)
}

func TestStore_MarkOperationFailed_DoesNotOverrideProcessed(t *testing.T) {
	ctx := context.Background()
	pool := newPool(t)
	store := database.NewStore(pool)

	accountID, productID := seedReferences(t, pool, "Moderate", 3)
	opID := seedOperation(t, pool, accountID, productID, "Buy", "100", "10")
	_, err := pool.Exec(ctx, `UPDATE operations SET status='Processed' WHERE id=$1;`, opID)
	require.NoError(t, err)

	require.NoError(t, store.WithinTx(ctx, func(tx operations.TxRepo) error {
		return tx.MarkOperationFailed(ctx, opID)
	}))

	var status string
	require.NoError(t, pool.QueryRow(ctx, `SELECT status FROM operations WHERE id=$1;`, opID).Scan(&status))
	require.Equal(t, "Processed", status, "o guard do WHERE não deve sobrescrever um resultado de negócio")
}

func TestStore_InsertProcessingEvent_PersistsAttemptAndError(t *testing.T) {
	ctx := context.Background()
	pool := newPool(t)
	store := database.NewStore(pool)

	accountID, productID := seedReferences(t, pool, "Moderate", 3)
	opID := seedOperation(t, pool, accountID, productID, "Buy", "100", "10")

	require.NoError(t, store.WithinTx(ctx, func(tx operations.TxRepo) error {
		return tx.InsertProcessingEvent(ctx, operations.ProcessingEvent{
			OperationID:  opID,
			EventType:    operations.EventOperationFailed,
			Status:       operations.StatusFailed,
			Payload:      `{"error":"boom"}`,
			AttemptCount: 3,
			ErrorMessage: "boom",
		})
	}))

	var attempt int
	var errMsg *string
	require.NoError(t, pool.QueryRow(ctx,
		`SELECT attempt_count, error_message FROM processing_events WHERE operation_id=$1 AND event_type='OperationFailed';`,
		opID).Scan(&attempt, &errMsg))
	require.Equal(t, 3, attempt)
	require.NotNil(t, errMsg)
	require.Equal(t, "boom", *errMsg)
}

func TestStore_InsertProcessingEvent_EmptyErrorIsNull(t *testing.T) {
	ctx := context.Background()
	pool := newPool(t)
	store := database.NewStore(pool)

	accountID, productID := seedReferences(t, pool, "Moderate", 3)
	opID := seedOperation(t, pool, accountID, productID, "Buy", "100", "10")

	require.NoError(t, store.WithinTx(ctx, func(tx operations.TxRepo) error {
		return tx.InsertProcessingEvent(ctx, operations.ProcessingEvent{
			OperationID: opID,
			EventType:   operations.EventOperationProcessed,
			Status:      operations.StatusProcessed,
			Payload:     `{"outcome":"Processed"}`,
		})
	}))

	var attempt int
	var errMsg *string
	require.NoError(t, pool.QueryRow(ctx,
		`SELECT attempt_count, error_message FROM processing_events WHERE operation_id=$1 AND event_type='OperationProcessed';`,
		opID).Scan(&attempt, &errMsg))
	require.Equal(t, 0, attempt)
	require.Nil(t, errMsg, "error_message vazio deve gravar NULL (mantém Processed/Rejected inalterados)")
}

func TestStore_RejectSellInsufficient(t *testing.T) {
	ctx := context.Background()
	pool := newPool(t)
	processor := operations.NewProcessor(database.NewStore(pool), discard())

	accountID, productID := seedReferences(t, pool, "Aggressive", 1)
	opID := seedOperation(t, pool, accountID, productID, "Sell", "10", "5")

	require.NoError(t, processor.Process(ctx, event(opID)))

	var status, reason string
	require.NoError(t, pool.QueryRow(ctx, `SELECT status, rejection_reason FROM operations WHERE id=$1;`, opID).Scan(&status, &reason))
	require.Equal(t, "Rejected", status)
	require.Equal(t, "InsufficientPosition", reason)
}
