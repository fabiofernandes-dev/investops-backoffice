package rules_test

import (
	"testing"

	"investops/worker/internal/rules"

	"github.com/stretchr/testify/assert"
)

func TestSuitabilityViolated(t *testing.T) {
	// Conservative permite até 2
	assert.False(t, rules.SuitabilityViolated("Conservative", 2))
	assert.True(t, rules.SuitabilityViolated("Conservative", 3))
	// Moderate permite até 4
	assert.False(t, rules.SuitabilityViolated("Moderate", 4))
	assert.True(t, rules.SuitabilityViolated("Moderate", 5))
	// Aggressive permite até 5
	assert.False(t, rules.SuitabilityViolated("Aggressive", 5))
	// perfil desconhecido: max 0 → qualquer risco viola
	assert.True(t, rules.SuitabilityViolated("Unknown", 1))
}
