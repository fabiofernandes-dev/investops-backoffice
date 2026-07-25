package operations

import (
	"context"
	"encoding/json"
	"errors"
	"log/slog"
	"testing"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

type recordingTx struct {
	failRows   int64
	failedID   uuid.UUID
	failCalled bool
	procEvents []ProcessingEvent
}

func (t *recordingTx) GetPositionForUpdate(_ context.Context, _, _ uuid.UUID) (Position, bool, error) {
	return Position{}, false, nil
}
func (t *recordingTx) UpsertPosition(_ context.Context, _, _ uuid.UUID, _ Position) error { return nil }
func (t *recordingTx) UpdateOperationStatus(_ context.Context, _ uuid.UUID, _, _ string) error {
	return nil
}
func (t *recordingTx) MarkOperationFailed(_ context.Context, id uuid.UUID) (int64, error) {
	t.failCalled = true
	t.failedID = id
	return t.failRows, nil
}
func (t *recordingTx) InsertProcessingEvent(_ context.Context, e ProcessingEvent) error {
	t.procEvents = append(t.procEvents, e)
	return nil
}
func (t *recordingTx) InsertOutbox(_ context.Context, _ OutboxMessage) error { return nil }

type recordingStore struct {
	tx             *recordingTx
	withinTxCalled bool
}

func (s *recordingStore) GetOperation(_ context.Context, _ uuid.UUID) (Operation, bool, error) {
	return Operation{}, false, nil
}
func (s *recordingStore) GetCustomerProfileByAccount(_ context.Context, _ uuid.UUID) (string, error) {
	return "", nil
}
func (s *recordingStore) GetProductRiskLevel(_ context.Context, _ uuid.UUID) (int, error) {
	return 0, nil
}
func (s *recordingStore) WithinTx(ctx context.Context, fn func(TxRepo) error) error {
	s.withinTxCalled = true
	return fn(s.tx)
}

func importedEventBody(t *testing.T, opID uuid.UUID) []byte {
	t.Helper()
	body, err := json.Marshal(OperationImportedEvent{
		CorrelationID: uuid.New(),
		Payload:       OperationImportedPayload{OperationID: opID},
	})
	require.NoError(t, err)
	return body
}

func silentLogger() *slog.Logger { return slog.New(slog.DiscardHandler) }

func TestFailureRecorder_ValidBody_MarksFailedAndRecordsEvent(t *testing.T) {
	opID := uuid.New()
	tx := &recordingTx{failRows: 1}
	store := &recordingStore{tx: tx}
	recorder := NewFailureRecorder(store, silentLogger())

	recorder.MarkExhausted(context.Background(), importedEventBody(t, opID), 3, errors.New("boom"))

	assert.True(t, store.withinTxCalled, "deve abrir transação para body válido")
	assert.True(t, tx.failCalled)
	assert.Equal(t, opID, tx.failedID)

	require.Len(t, tx.procEvents, 1)
	evt := tx.procEvents[0]
	assert.Equal(t, opID, evt.OperationID)
	assert.Equal(t, EventOperationFailed, evt.EventType)
	assert.Equal(t, StatusFailed, evt.Status)
	assert.Equal(t, 3, evt.AttemptCount)
	assert.Equal(t, "boom", evt.ErrorMessage)
}

func TestFailureRecorder_GuardBlocked_SkipsEvent(t *testing.T) {
	tx := &recordingTx{failRows: 0}
	store := &recordingStore{tx: tx}
	recorder := NewFailureRecorder(store, silentLogger())

	recorder.MarkExhausted(context.Background(), importedEventBody(t, uuid.New()), 3, errors.New("boom"))

	assert.True(t, tx.failCalled)
	assert.Empty(t, tx.procEvents, "guard barrou a transição: não deve gravar processing_event espúrio")
}

func TestFailureRecorder_NilCause_DoesNotPanic(t *testing.T) {
	opID := uuid.New()
	tx := &recordingTx{failRows: 1}
	store := &recordingStore{tx: tx}
	recorder := NewFailureRecorder(store, silentLogger())

	recorder.MarkExhausted(context.Background(), importedEventBody(t, opID), 3, nil)

	require.Len(t, tx.procEvents, 1)
	assert.Equal(t, "", tx.procEvents[0].ErrorMessage)
}

func TestFailureRecorder_InvalidBody_DoesNotOpenTx(t *testing.T) {
	tx := &recordingTx{}
	store := &recordingStore{tx: tx}
	recorder := NewFailureRecorder(store, silentLogger())

	recorder.MarkExhausted(context.Background(), []byte(`{not json`), 3, errors.New("boom"))

	assert.False(t, store.withinTxCalled, "body inválido não deve abrir transação")
	assert.False(t, tx.failCalled)
	assert.Empty(t, tx.procEvents)
}
