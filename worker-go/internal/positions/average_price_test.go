package positions_test

import (
	"testing"

	"investops/worker/internal/positions"

	"github.com/shopspring/decimal"
	"github.com/stretchr/testify/assert"
)

func TestNewAveragePrice(t *testing.T) {
	d := decimal.RequireFromString

	// 100@10 + 100@20 → 150 unidades a média 15
	got := positions.NewAveragePrice(d("100"), d("10"), d("100"), d("20"))
	assert.True(t, got.Equal(d("15")), "esperado 15, obtido %s", got)

	// primeira compra (sem posição) → preço de compra
	first := positions.NewAveragePrice(d("0"), d("0"), d("50"), d("25.40"))
	assert.True(t, first.Equal(d("25.40")), "esperado 25.40, obtido %s", first)
}
