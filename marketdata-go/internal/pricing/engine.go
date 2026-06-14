package pricing

import (
	"hash/fnv"
	"math/rand"
	"sync"

	"github.com/shopspring/decimal"
)

// Engine gera cotações simuladas: preço-base determinístico por produto + random walk.
// Mantém o preço atual em memória (efêmero), protegido por mutex.
type Engine struct {
	mu      sync.Mutex
	current map[string]decimal.Decimal
}

func New() *Engine {
	return &Engine{current: make(map[string]decimal.Decimal)}
}

// BasePrice é determinístico: o mesmo productId sempre dá o mesmo preço-base (10.00 .. 509.99).
func BasePrice(productID string) decimal.Decimal {
	h := fnv.New32a()
	_, _ = h.Write([]byte(productID))
	cents := int64(1000 + h.Sum32()%49000)
	return decimal.NewFromInt(cents).Div(decimal.NewFromInt(100))
}

// Quote retorna o preço atual do produto (base, se ainda não cotado) sem avançar o walk.
func (e *Engine) Quote(productID string) decimal.Decimal {
	e.mu.Lock()
	defer e.mu.Unlock()
	if p, ok := e.current[productID]; ok {
		return p
	}
	p := BasePrice(productID)
	e.current[productID] = p
	return p
}

// Next avança o random walk (±0.5%) e retorna o novo preço, nunca <= 0.
func (e *Engine) Next(productID string) decimal.Decimal {
	e.mu.Lock()
	defer e.mu.Unlock()
	cur, ok := e.current[productID]
	if !ok {
		cur = BasePrice(productID)
	}
	factor := decimal.NewFromFloat((rand.Float64()*2 - 1) * 0.005)
	next := cur.Add(cur.Mul(factor)).Round(2)
	if next.IsZero() || next.IsNegative() {
		next = BasePrice(productID)
	}
	e.current[productID] = next
	return next
}
