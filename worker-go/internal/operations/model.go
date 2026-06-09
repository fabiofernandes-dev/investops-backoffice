package operations

import (
	"github.com/google/uuid"
	"github.com/shopspring/decimal"
)

// Tipos de operação (espelham o enum OperationType do domínio .NET).
const (
	TypeBuy = "Buy"
	TypeSell = "Sell"
)

// Status de operação relevantes ao worker.
const (
	StatusImported  = "Imported"
	StatusProcessed = "Processed"
	StatusRejected  = "Rejected"
)

// Motivos de rejeição (espelham RejectionReason do domínio .NET).
const (
	ReasonInvestorProfileNotCompatible = "InvestorProfileNotCompatible"
	ReasonInsufficientPosition         = "InsufficientPosition"
)

type Operation struct {
	ID         uuid.UUID
	ExternalID string
	AccountID  uuid.UUID
	ProductID  uuid.UUID
	Type       string
	Quantity   decimal.Decimal
	UnitPrice  decimal.Decimal
	Status     string
}

type Position struct {
	Quantity     decimal.Decimal
	AveragePrice decimal.Decimal
}

func (p Position) GrossAmount() decimal.Decimal {
	return p.Quantity.Mul(p.AveragePrice)
}

// Outcome é o resultado puro da decisão de processamento.
type Outcome struct {
	Rejected        bool
	Reason          string
	NewPosition     Position
	PositionChanged bool
}
