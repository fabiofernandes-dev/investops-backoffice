package operations

import (
	"investops/worker/internal/positions"
	"investops/worker/internal/rules"
)

// Decide aplica as regras de negócio (SPEC §6) e retorna o desfecho do processamento.
// Função pura: não toca em I/O. `posExists=false` representa posição inexistente (quantidade 0).
func Decide(op Operation, profile string, riskLevel int, pos Position, posExists bool) Outcome {
	switch op.Type {
	case TypeBuy:
		if rules.SuitabilityViolated(profile, riskLevel) {
			return Outcome{Rejected: true, Reason: ReasonInvestorProfileNotCompatible}
		}
		newQty := pos.Quantity.Add(op.Quantity)
		newAvg := positions.NewAveragePrice(pos.Quantity, pos.AveragePrice, op.Quantity, op.UnitPrice)
		return Outcome{
			NewPosition:     Position{Quantity: newQty, AveragePrice: newAvg},
			PositionChanged: true,
		}

	case TypeSell:
		if !posExists || rules.InsufficientPosition(pos.Quantity, op.Quantity) {
			return Outcome{Rejected: true, Reason: ReasonInsufficientPosition}
		}
		// venda: preço médio permanece, quantidade diminui
		newQty := pos.Quantity.Sub(op.Quantity)
		return Outcome{
			NewPosition:     Position{Quantity: newQty, AveragePrice: pos.AveragePrice},
			PositionChanged: true,
		}

	default:
		// Deposit/Withdraw/Income/Fee: processa sem alterar posição (simplificação do MVP)
		return Outcome{PositionChanged: false}
	}
}
