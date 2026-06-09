package operations_test

import (
	"testing"

	"investops/worker/internal/operations"

	"github.com/shopspring/decimal"
	"github.com/stretchr/testify/assert"
)

func buy(qty, price string) operations.Operation {
	return operations.Operation{Type: operations.TypeBuy, Quantity: decimal.RequireFromString(qty), UnitPrice: decimal.RequireFromString(price)}
}

func sell(qty, price string) operations.Operation {
	return operations.Operation{Type: operations.TypeSell, Quantity: decimal.RequireFromString(qty), UnitPrice: decimal.RequireFromString(price)}
}

func pos(qty, avg string) operations.Position {
	return operations.Position{Quantity: decimal.RequireFromString(qty), AveragePrice: decimal.RequireFromString(avg)}
}

func TestDecide_BuyValid_FirstPurchase(t *testing.T) {
	out := operations.Decide(buy("100", "25.40"), "Moderate", 3, operations.Position{}, false)

	assert.False(t, out.Rejected)
	assert.True(t, out.PositionChanged)
	assert.True(t, out.NewPosition.Quantity.Equal(decimal.RequireFromString("100")))
	assert.True(t, out.NewPosition.AveragePrice.Equal(decimal.RequireFromString("25.40")))
}

func TestDecide_BuyValid_AveragesUp(t *testing.T) {
	out := operations.Decide(buy("100", "20"), "Aggressive", 5, pos("100", "10"), true)

	assert.False(t, out.Rejected)
	assert.True(t, out.NewPosition.Quantity.Equal(decimal.RequireFromString("200")))
	assert.True(t, out.NewPosition.AveragePrice.Equal(decimal.RequireFromString("15")))
}

func TestDecide_BuyRejectedBySuitability(t *testing.T) {
	out := operations.Decide(buy("10", "5"), "Conservative", 5, operations.Position{}, false)

	assert.True(t, out.Rejected)
	assert.Equal(t, operations.ReasonInvestorProfileNotCompatible, out.Reason)
	assert.False(t, out.PositionChanged)
}

func TestDecide_SellValid_KeepsAverage(t *testing.T) {
	out := operations.Decide(sell("40", "30"), "Moderate", 3, pos("100", "10"), true)

	assert.False(t, out.Rejected)
	assert.True(t, out.NewPosition.Quantity.Equal(decimal.RequireFromString("60")))
	assert.True(t, out.NewPosition.AveragePrice.Equal(decimal.RequireFromString("10"))) // média inalterada
}

func TestDecide_SellRejected_InsufficientPosition(t *testing.T) {
	out := operations.Decide(sell("150", "30"), "Moderate", 3, pos("100", "10"), true)

	assert.True(t, out.Rejected)
	assert.Equal(t, operations.ReasonInsufficientPosition, out.Reason)
}

func TestDecide_SellRejected_NoPosition(t *testing.T) {
	out := operations.Decide(sell("1", "30"), "Moderate", 3, operations.Position{}, false)

	assert.True(t, out.Rejected)
	assert.Equal(t, operations.ReasonInsufficientPosition, out.Reason)
}

func TestDecide_OtherType_ProcessedNoPositionChange(t *testing.T) {
	op := operations.Operation{Type: "Deposit", Quantity: decimal.RequireFromString("1"), UnitPrice: decimal.RequireFromString("1")}
	out := operations.Decide(op, "Moderate", 3, operations.Position{}, false)

	assert.False(t, out.Rejected)
	assert.False(t, out.PositionChanged)
}
