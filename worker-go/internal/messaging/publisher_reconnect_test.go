package messaging

import (
	"context"
	"errors"
	"sync/atomic"
	"testing"

	"github.com/google/uuid"
	amqp "github.com/rabbitmq/amqp091-go"
	"github.com/stretchr/testify/require"
)

type stubChannel struct {
	publishErr error
	published  *int32
	closes     *int32
}

func (s *stubChannel) PublishWithContext(_ context.Context, _, _ string, _, _ bool, _ amqp.Publishing) error {
	if s.publishErr != nil {
		return s.publishErr
	}
	if s.published != nil {
		atomic.AddInt32(s.published, 1)
	}
	return nil
}

func (s *stubChannel) Close() error {
	if s.closes != nil {
		atomic.AddInt32(s.closes, 1)
	}
	return nil
}

// Prova que, quando um envio falha (canal caído), o publisher fecha o canal morto e o próximo
// lote reconecta e publica — em vez de ficar preso no canal morto até reiniciar o processo.
func TestPublisherReconnectsAfterPublishFailure(t *testing.T) {
	var connects, closes, published int32
	failing := &stubChannel{publishErr: errors.New("canal fechado inesperadamente"), closes: &closes}
	working := &stubChannel{published: &published, closes: &closes}
	channels := []amqpChannel{failing, working}

	p := &Publisher{
		exchange: "investops",
		logger:   discardLogger(),
		connect: func() (amqpChannel, error) {
			i := atomic.AddInt32(&connects, 1) - 1
			return channels[i], nil
		},
	}
	rows := []pendingRow{{id: uuid.New(), eventType: "OperationProcessed", payload: `{}`}}

	pub, failed, err := p.publishBatch(context.Background(), rows)
	require.Error(t, err)
	require.NotNil(t, failed)
	require.Empty(t, pub)
	require.Equal(t, int32(1), atomic.LoadInt32(&connects))
	require.Equal(t, int32(1), atomic.LoadInt32(&closes), "o canal caído deve ser fechado")

	pub, failed, err = p.publishBatch(context.Background(), rows)
	require.NoError(t, err)
	require.Nil(t, failed)
	require.Len(t, pub, 1)
	require.Equal(t, int32(2), atomic.LoadInt32(&connects), "deve reconectar no próximo lote")
	require.Equal(t, int32(1), atomic.LoadInt32(&published))
}

// Prova que uma falha de conexão inicial (broker fora no startup) não fatala nem cacheia um
// canal nulo: uma tentativa posterior reconecta normalmente.
func TestPublisherEnsureChannelRecoversAfterDialFailure(t *testing.T) {
	var connects int32
	p := &Publisher{
		logger: discardLogger(),
		connect: func() (amqpChannel, error) {
			if atomic.AddInt32(&connects, 1) < 2 {
				return nil, errors.New("rabbitmq indisponível")
			}
			return &stubChannel{}, nil
		},
	}

	require.Error(t, p.ensureChannel())
	require.Nil(t, p.ch)
	require.NoError(t, p.ensureChannel())
	require.NotNil(t, p.ch)
	require.Equal(t, int32(2), atomic.LoadInt32(&connects))
}
