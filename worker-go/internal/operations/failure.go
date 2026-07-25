package operations

import (
	"context"
	"encoding/json"
	"log/slog"
)

type FailureRecorder struct {
	store  Store
	logger *slog.Logger
}

func NewFailureRecorder(store Store, logger *slog.Logger) *FailureRecorder {
	return &FailureRecorder{store: store, logger: logger}
}

// MarkExhausted casa com messaging.OnExhaustedFunc: ao esgotar os retries, marca a
// operação como Failed e grava um processing_event OperationFailed na mesma transação.
func (r *FailureRecorder) MarkExhausted(ctx context.Context, body []byte, attempt int, cause error) {
	var evt OperationImportedEvent
	if err := json.Unmarshal(body, &evt); err != nil {
		r.logger.Error("evento inválido no esgotamento; ignorando", "error", err)
		return
	}
	opID := evt.Payload.OperationID
	errMsg := ""
	if cause != nil {
		errMsg = cause.Error()
	}
	if err := r.store.WithinTx(ctx, func(tx TxRepo) error {
		affected, err := tx.MarkOperationFailed(ctx, opID)
		if err != nil {
			return err
		}
		if affected == 0 {
			// guard no SQL barrou a transição (op já Processed/Rejected): não gravar evento espúrio.
			return nil
		}
		return tx.InsertProcessingEvent(ctx, ProcessingEvent{
			OperationID:  opID,
			EventType:    EventOperationFailed,
			Status:       StatusFailed,
			AttemptCount: attempt,
			ErrorMessage: errMsg,
			Payload:      summary(map[string]string{"error": errMsg}),
		})
	}); err != nil {
		r.logger.Error("falha ao registrar operação como Failed", "operationId", opID, "error", err)
	}
}
