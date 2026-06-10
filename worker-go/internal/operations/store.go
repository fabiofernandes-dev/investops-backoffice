package operations

import (
	"context"

	"github.com/google/uuid"
)

// Store dá acesso de leitura e à unidade de trabalho transacional.
type Store interface {
	GetOperation(ctx context.Context, id uuid.UUID) (Operation, bool, error)
	GetCustomerProfileByAccount(ctx context.Context, accountID uuid.UUID) (string, error)
	GetProductRiskLevel(ctx context.Context, productID uuid.UUID) (int, error)
	WithinTx(ctx context.Context, fn func(TxRepo) error) error
}

// TxRepo expõe as escritas do processamento, todas na mesma transação.
type TxRepo interface {
	GetPositionForUpdate(ctx context.Context, accountID, productID uuid.UUID) (Position, bool, error)
	UpsertPosition(ctx context.Context, accountID, productID uuid.UUID, p Position) error
	UpdateOperationStatus(ctx context.Context, id uuid.UUID, status, reason string) error
	InsertProcessingEvent(ctx context.Context, e ProcessingEvent) error
	InsertOutbox(ctx context.Context, m OutboxMessage) error
}

type ProcessingEvent struct {
	OperationID uuid.UUID
	EventType   string
	Status      string
	Payload     string
}

type OutboxMessage struct {
	EventType string
	Version   int
	Payload   string
}
