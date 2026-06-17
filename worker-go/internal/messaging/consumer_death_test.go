package messaging

import (
	"testing"

	amqp "github.com/rabbitmq/amqp091-go"
	"github.com/stretchr/testify/require"
)

func TestDeathCount(t *testing.T) {
	t.Run("sem header x-death retorna 0", func(t *testing.T) {
		require.Equal(t, int64(0), deathCount(amqp.Delivery{}, "operations.processing"))
	})

	t.Run("filtra por queue+reason (nao usa indice 0)", func(t *testing.T) {
		d := amqp.Delivery{Headers: amqp.Table{
			"x-death": []interface{}{
				amqp.Table{"queue": "operations.retry", "reason": "expired", "count": int64(2)},
				amqp.Table{"queue": "operations.processing", "reason": "rejected", "count": int64(2)},
			},
		}}
		require.Equal(t, int64(2), deathCount(d, "operations.processing"))
	})

	t.Run("sem entrada correspondente retorna 0", func(t *testing.T) {
		d := amqp.Delivery{Headers: amqp.Table{
			"x-death": []interface{}{
				amqp.Table{"queue": "outra.fila", "reason": "expired", "count": int64(5)},
			},
		}}
		require.Equal(t, int64(0), deathCount(d, "operations.processing"))
	})
}
