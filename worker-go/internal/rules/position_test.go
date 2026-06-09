package rules_test

import (
	"testing"

	"investops/worker/internal/rules"

	"github.com/shopspring/decimal"
	"github.com/stretchr/testify/assert"
)

func TestInsufficientPosition(t *testing.T) {
	d := decimal.RequireFromString
	assert.True(t, rules.InsufficientPosition(d("100"), d("150")))  // 100 < 150
	assert.False(t, rules.InsufficientPosition(d("100"), d("100"))) // exato
	assert.False(t, rules.InsufficientPosition(d("100"), d("50")))
	assert.True(t, rules.InsufficientPosition(d("0"), d("1")))      // posição inexistente
}
