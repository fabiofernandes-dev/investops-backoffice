package operations

import (
	"time"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"
)

// OperationImportedEvent é o evento de entrada (camelCase, igual ao emitido pela API .NET).
type OperationImportedEvent struct {
	EventID       uuid.UUID                `json:"eventId"`
	EventType     string                   `json:"eventType"`
	Version       int                      `json:"version"`
	OccurredAt    time.Time                `json:"occurredAt"`
	CorrelationID uuid.UUID                `json:"correlationId"`
	Payload       OperationImportedPayload `json:"payload"`
}

type OperationImportedPayload struct {
	OperationID   uuid.UUID       `json:"operationId"`
	ExternalID    string          `json:"externalId"`
	AccountID     uuid.UUID       `json:"accountId"`
	ProductID     uuid.UUID       `json:"productId"`
	OperationType string          `json:"operationType"`
	Quantity      decimal.Decimal `json:"quantity"`
	UnitPrice     decimal.Decimal `json:"unitPrice"`
	OperationDate string          `json:"operationDate"`
}
