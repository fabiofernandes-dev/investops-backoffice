package operations

import (
	"encoding/json"
	"time"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"
)

const (
	EventOperationProcessed = "OperationProcessed"
	EventOperationRejected  = "OperationRejected"
	EventPositionUpdated    = "PositionUpdated"
	eventVersion            = 1
)

type envelope struct {
	EventID       uuid.UUID `json:"eventId"`
	EventType     string    `json:"eventType"`
	Version       int       `json:"version"`
	OccurredAt    time.Time `json:"occurredAt"`
	CorrelationID uuid.UUID `json:"correlationId"`
	Payload       any       `json:"payload"`
}

type operationProcessedPayload struct {
	OperationID  uuid.UUID       `json:"operationId"`
	AccountID    uuid.UUID       `json:"accountId"`
	ProductID    uuid.UUID       `json:"productId"`
	Quantity     decimal.Decimal `json:"quantity"`
	AveragePrice decimal.Decimal `json:"averagePrice"`
	GrossAmount  decimal.Decimal `json:"grossAmount"`
}

type operationRejectedPayload struct {
	OperationID uuid.UUID `json:"operationId"`
	Reason      string    `json:"reason"`
}

type positionUpdatedPayload struct {
	AccountID    uuid.UUID       `json:"accountId"`
	ProductID    uuid.UUID       `json:"productId"`
	Quantity     decimal.Decimal `json:"quantity"`
	AveragePrice decimal.Decimal `json:"averagePrice"`
	GrossAmount  decimal.Decimal `json:"grossAmount"`
}

func outboxFor(eventType string, correlationID uuid.UUID, payload any) (OutboxMessage, error) {
	body, err := json.Marshal(envelope{
		EventID:       uuid.New(),
		EventType:     eventType,
		Version:       eventVersion,
		OccurredAt:    time.Now().UTC(),
		CorrelationID: correlationID,
		Payload:       payload,
	})
	if err != nil {
		return OutboxMessage{}, err
	}
	return OutboxMessage{EventType: eventType, Version: eventVersion, Payload: string(body)}, nil
}

func operationProcessedOutbox(op Operation, pos Position, correlationID uuid.UUID) (OutboxMessage, error) {
	return outboxFor(EventOperationProcessed, correlationID, operationProcessedPayload{
		OperationID:  op.ID,
		AccountID:    op.AccountID,
		ProductID:    op.ProductID,
		Quantity:     pos.Quantity,
		AveragePrice: pos.AveragePrice,
		GrossAmount:  pos.GrossAmount(),
	})
}

func operationRejectedOutbox(op Operation, reason string, correlationID uuid.UUID) (OutboxMessage, error) {
	return outboxFor(EventOperationRejected, correlationID, operationRejectedPayload{
		OperationID: op.ID,
		Reason:      reason,
	})
}

func positionUpdatedOutbox(op Operation, pos Position, correlationID uuid.UUID) (OutboxMessage, error) {
	return outboxFor(EventPositionUpdated, correlationID, positionUpdatedPayload{
		AccountID:    op.AccountID,
		ProductID:    op.ProductID,
		Quantity:     pos.Quantity,
		AveragePrice: pos.AveragePrice,
		GrossAmount:  pos.GrossAmount(),
	})
}
