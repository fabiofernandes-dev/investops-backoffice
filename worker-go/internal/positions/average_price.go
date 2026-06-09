package positions

import "github.com/shopspring/decimal"

// NewAveragePrice calcula o novo preço médio em compras (SPEC §6 Regra 5).
// Primeira compra (curQty 0) resulta no próprio preço de compra.
func NewAveragePrice(curQty, curAvg, addQty, price decimal.Decimal) decimal.Decimal {
	newQty := curQty.Add(addQty)
	if newQty.IsZero() {
		return decimal.Zero
	}
	return curQty.Mul(curAvg).Add(addQty.Mul(price)).Div(newQty)
}
