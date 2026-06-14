package pricing

import (
	"testing"

	"github.com/shopspring/decimal"
	"github.com/stretchr/testify/assert"
)

func TestBasePriceIsDeterministic(t *testing.T) {
	a := BasePrice("44444444-4444-4444-4444-444444444444")
	b := BasePrice("44444444-4444-4444-4444-444444444444")
	assert.True(t, a.Equal(b))
	assert.True(t, a.GreaterThan(decimal.Zero))
}

func TestDifferentProductsHaveDifferentBase(t *testing.T) {
	a := BasePrice("aaaa")
	b := BasePrice("bbbb")
	assert.False(t, a.Equal(b))
}

func TestQuoteOnFreshEngineReturnsBase(t *testing.T) {
	e := New()
	assert.True(t, e.Quote("prod-1").Equal(BasePrice("prod-1")))
}

func TestNextStaysPositiveAndNearPrevious(t *testing.T) {
	e := New()
	prev := e.Quote("prod-1")
	for i := 0; i < 200; i++ {
		next := e.Next("prod-1")
		assert.True(t, next.GreaterThan(decimal.Zero), "preço deve ser > 0")
		// passo de ±0.5% por tick -> sempre dentro de ±2% do anterior
		diff := next.Sub(prev).Abs()
		assert.True(t, diff.LessThanOrEqual(prev.Mul(decimal.NewFromFloat(0.02))), "passo dentro do limite")
		prev = next
	}
}
