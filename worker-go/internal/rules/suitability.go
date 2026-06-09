package rules

// MaxRiskLevel mapeia o perfil do investidor ao nível máximo de risco permitido (SPEC §6 Regra 1).
func MaxRiskLevel(profile string) int {
	switch profile {
	case "Conservative":
		return 2
	case "Moderate":
		return 4
	case "Aggressive":
		return 5
	default:
		return 0
	}
}

// SuitabilityViolated indica se o produto excede o risco permitido para o perfil.
func SuitabilityViolated(profile string, riskLevel int) bool {
	return riskLevel > MaxRiskLevel(profile)
}
