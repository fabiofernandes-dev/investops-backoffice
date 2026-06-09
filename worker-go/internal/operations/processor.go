package operations

import (
	"context"
	"encoding/json"
	"log/slog"
	"time"

	"github.com/google/uuid"
)

type Processor struct {
	store  Store
	logger *slog.Logger
}

func NewProcessor(store Store, logger *slog.Logger) *Processor {
	return &Processor{store: store, logger: logger}
}

func (p *Processor) Process(ctx context.Context, evt OperationImportedEvent) error {
	start := time.Now()
	log := p.logger.With("correlationId", evt.CorrelationID, "operationId", evt.Payload.OperationID)

	op, found, err := p.store.GetOperation(ctx, evt.Payload.OperationID)
	if err != nil {
		return err
	}
	if !found {
		log.Warn("operação não encontrada; descartando evento")
		return nil
	}
	if op.Status == StatusProcessed || op.Status == StatusRejected {
		log.Info("operação já finalizada; ignorando (idempotência)", "status", op.Status)
		return nil
	}

	profile, err := p.store.GetCustomerProfileByAccount(ctx, op.AccountID)
	if err != nil {
		return err
	}
	riskLevel, err := p.store.GetProductRiskLevel(ctx, op.ProductID)
	if err != nil {
		return err
	}

	var outcome Outcome
	err = p.store.WithinTx(ctx, func(tx TxRepo) error {
		pos, posExists, err := tx.GetPositionForUpdate(ctx, op.AccountID, op.ProductID)
		if err != nil {
			return err
		}
		outcome = Decide(op, profile, riskLevel, pos, posExists)
		return p.apply(ctx, tx, op, outcome, evt.CorrelationID)
	})
	if err != nil {
		return err
	}

	log.Info("operação processada",
		"rejected", outcome.Rejected,
		"reason", outcome.Reason,
		"durationMs", time.Since(start).Milliseconds())
	return nil
}

func (p *Processor) apply(ctx context.Context, tx TxRepo, op Operation, outcome Outcome, correlationID uuid.UUID) error {
	if outcome.Rejected {
		if err := tx.UpdateOperationStatus(ctx, op.ID, StatusRejected, outcome.Reason); err != nil {
			return err
		}
		if err := tx.InsertProcessingEvent(ctx, ProcessingEvent{
			OperationID: op.ID,
			EventType:   EventOperationRejected,
			Status:      StatusRejected,
			Payload:     summary(map[string]string{"reason": outcome.Reason}),
		}); err != nil {
			return err
		}
		rejected, err := operationRejectedOutbox(op, outcome.Reason, correlationID)
		if err != nil {
			return err
		}
		return tx.InsertOutbox(ctx, rejected)
	}

	if outcome.PositionChanged {
		if err := tx.UpsertPosition(ctx, op.AccountID, op.ProductID, outcome.NewPosition); err != nil {
			return err
		}
	}
	if err := tx.UpdateOperationStatus(ctx, op.ID, StatusProcessed, ""); err != nil {
		return err
	}
	if err := tx.InsertProcessingEvent(ctx, ProcessingEvent{
		OperationID: op.ID,
		EventType:   EventOperationProcessed,
		Status:      StatusProcessed,
		Payload:     summary(map[string]string{"outcome": StatusProcessed}),
	}); err != nil {
		return err
	}

	processed, err := operationProcessedOutbox(op, outcome.NewPosition, correlationID)
	if err != nil {
		return err
	}
	if err := tx.InsertOutbox(ctx, processed); err != nil {
		return err
	}

	if outcome.PositionChanged {
		positionUpdated, err := positionUpdatedOutbox(op, outcome.NewPosition, correlationID)
		if err != nil {
			return err
		}
		if err := tx.InsertOutbox(ctx, positionUpdated); err != nil {
			return err
		}
	}
	return nil
}

func summary(m map[string]string) string {
	body, _ := json.Marshal(m)
	return string(body)
}
