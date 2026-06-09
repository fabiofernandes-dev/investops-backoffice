package rules

import "github.com/shopspring/decimal"

// InsufficientPosition indica se uma venda deixaria a posição negativa (SPEC §6 Regra 2).
func InsufficientPosition(positionQty, sellQty decimal.Decimal) bool {
	return positionQty.LessThan(sellQty)
}
