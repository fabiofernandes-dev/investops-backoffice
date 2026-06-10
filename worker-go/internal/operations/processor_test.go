package operations_test

import (
	"context"
	"log/slog"
	"testing"

	"investops/worker/internal/operations"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

type fakeTx struct {
	pos           operations.Position
	posExists     bool
	upserts       []operations.Position
	statusUpdates []statusUpdate
	procEvents    []operations.ProcessingEvent
	outbox        []operations.OutboxMessage
}

type statusUpdate struct {
	status string
	reason string
}

func (f *fakeTx) GetPositionForUpdate(_ context.Context, _, _ uuid.UUID) (operations.Position, bool, error) {
	return f.pos, f.posExists, nil
}
func (f *fakeTx) UpsertPosition(_ context.Context, _, _ uuid.UUID, p operations.Position) error {
	f.upserts = append(f.upserts, p)
	return nil
}
func (f *fakeTx) UpdateOperationStatus(_ context.Context, _ uuid.UUID, status, reason string) error {
	f.statusUpdates = append(f.statusUpdates, statusUpdate{status, reason})
	return nil
}
func (f *fakeTx) InsertProcessingEvent(_ context.Context, e operations.ProcessingEvent) error {
	f.procEvents = append(f.procEvents, e)
	return nil
}
func (f *fakeTx) InsertOutbox(_ context.Context, m operations.OutboxMessage) error {
	f.outbox = append(f.outbox, m)
	return nil
}

type fakeStore struct {
	op             operations.Operation
	found          bool
	profile        string
	risk           int
	tx             *fakeTx
	withinTxCalled bool
}

func (f *fakeStore) GetOperation(_ context.Context, _ uuid.UUID) (operations.Operation, bool, error) {
	return f.op, f.found, nil
}
func (f *fakeStore) GetCustomerProfileByAccount(_ context.Context, _ uuid.UUID) (string, error) {
	return f.profile, nil
}
func (f *fakeStore) GetProductRiskLevel(_ context.Context, _ uuid.UUID) (int, error) {
	return f.risk, nil
}
func (f *fakeStore) WithinTx(_ context.Context, fn func(operations.TxRepo) error) error {
	f.withinTxCalled = true
	return fn(f.tx)
}

func event(operationID uuid.UUID) operations.OperationImportedEvent {
	return operations.OperationImportedEvent{
		CorrelationID: uuid.New(),
		Payload:       operations.OperationImportedPayload{OperationID: operationID},
	}
}

func newOp(opType, status string) operations.Operation {
	return operations.Operation{
		ID: uuid.New(), AccountID: uuid.New(), ProductID: uuid.New(),
		Type: opType, Status: status,
		Quantity: decimal.RequireFromString("100"), UnitPrice: decimal.RequireFromString("10"),
	}
}

func discardLogger() *slog.Logger { return slog.New(slog.DiscardHandler) }

func TestProcessor_SkipsAlreadyFinalized(t *testing.T) {
	store := &fakeStore{op: newOp(operations.TypeBuy, operations.StatusProcessed), found: true, tx: &fakeTx{}}
	p := operations.NewProcessor(store, discardLogger())

	require.NoError(t, p.Process(context.Background(), event(store.op.ID)))
	assert.False(t, store.withinTxCalled, "não deve abrir transação para operação já finalizada")
}

func TestProcessor_BuyValid_PersistsAndPublishes(t *testing.T) {
	store := &fakeStore{
		op: newOp(operations.TypeBuy, operations.StatusImported), found: true,
		profile: "Moderate", risk: 3, tx: &fakeTx{posExists: false},
	}
	p := operations.NewProcessor(store, discardLogger())

	require.NoError(t, p.Process(context.Background(), event(store.op.ID)))

	require.Len(t, store.tx.upserts, 1)
	require.Len(t, store.tx.statusUpdates, 1)
	assert.Equal(t, operations.StatusProcessed, store.tx.statusUpdates[0].status)
	// outbox: OperationProcessed + PositionUpdated
	require.Len(t, store.tx.outbox, 2)
	assert.Equal(t, operations.EventOperationProcessed, store.tx.outbox[0].EventType)
	assert.Equal(t, operations.EventPositionUpdated, store.tx.outbox[1].EventType)
}

func TestProcessor_RejectedBySuitability_NoPositionChange(t *testing.T) {
	store := &fakeStore{
		op: newOp(operations.TypeBuy, operations.StatusImported), found: true,
		profile: "Conservative", risk: 5, tx: &fakeTx{posExists: false},
	}
	p := operations.NewProcessor(store, discardLogger())

	require.NoError(t, p.Process(context.Background(), event(store.op.ID)))

	assert.Empty(t, store.tx.upserts, "rejeição não altera posição")
	require.Len(t, store.tx.statusUpdates, 1)
	assert.Equal(t, operations.StatusRejected, store.tx.statusUpdates[0].status)
	assert.Equal(t, operations.ReasonInvestorProfileNotCompatible, store.tx.statusUpdates[0].reason)
	require.Len(t, store.tx.outbox, 1)
	assert.Equal(t, operations.EventOperationRejected, store.tx.outbox[0].EventType)
}
