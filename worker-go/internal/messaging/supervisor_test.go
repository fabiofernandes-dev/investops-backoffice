package messaging

import (
	"context"
	"errors"
	"io"
	"log/slog"
	"sync/atomic"
	"testing"
	"time"

	"github.com/stretchr/testify/require"
)

func discardLogger() *slog.Logger {
	return slog.New(slog.NewTextHandler(io.Discard, nil))
}

type fakeSession struct {
	consume func(ctx context.Context, handler Handler) error
	closes  *int32
}

func (f fakeSession) Consume(ctx context.Context, handler Handler) error { return f.consume(ctx, handler) }

func (f fakeSession) Close() error {
	if f.closes != nil {
		atomic.AddInt32(f.closes, 1)
	}
	return nil
}

func blockUntilDone(ctx context.Context, _ Handler) error {
	<-ctx.Done()
	return ctx.Err()
}

func TestSuperviseConsumerRetriesInitialConnectThenStopsOnCancel(t *testing.T) {
	var attempts int32
	connect := func() (session, error) {
		if atomic.AddInt32(&attempts, 1) < 3 {
			return nil, errors.New("rabbitmq indisponível")
		}
		return fakeSession{consume: blockUntilDone}, nil
	}

	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan error, 1)
	go func() { done <- superviseConsumer(ctx, discardLogger(), connect, nil, time.Millisecond) }()

	require.Eventually(t, func() bool { return atomic.LoadInt32(&attempts) >= 3 }, 2*time.Second, 2*time.Millisecond,
		"deveria repetir a conexão inicial até obter sucesso")

	cancel()
	select {
	case err := <-done:
		require.NoError(t, err, "cancelamento do contexto deve encerrar limpo (nil)")
	case <-time.After(2 * time.Second):
		t.Fatal("superviseConsumer não retornou após o cancelamento")
	}
	require.GreaterOrEqual(t, atomic.LoadInt32(&attempts), int32(3))
}

func TestSuperviseConsumerReconnectsAfterTransientError(t *testing.T) {
	var consumeCalls, closes int32
	connect := func() (session, error) {
		return fakeSession{
			closes: &closes,
			consume: func(ctx context.Context, _ Handler) error {
				if atomic.AddInt32(&consumeCalls, 1) == 1 {
					return errors.New("canal de entregas fechado inesperadamente")
				}
				<-ctx.Done()
				return ctx.Err()
			},
		}, nil
	}

	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan error, 1)
	go func() { done <- superviseConsumer(ctx, discardLogger(), connect, nil, time.Millisecond) }()

	require.Eventually(t, func() bool { return atomic.LoadInt32(&consumeCalls) >= 2 }, 2*time.Second, 2*time.Millisecond,
		"deveria reconectar e voltar a consumir após erro transiente")

	cancel()
	select {
	case err := <-done:
		require.NoError(t, err)
	case <-time.After(2 * time.Second):
		t.Fatal("superviseConsumer não retornou após o cancelamento")
	}
	require.GreaterOrEqual(t, atomic.LoadInt32(&closes), int32(1), "cada sessão perdida deve ser fechada")
}

func TestSuperviseConsumerStopsWhenContextCancelledDuringConsume(t *testing.T) {
	var connects int32
	connect := func() (session, error) {
		atomic.AddInt32(&connects, 1)
		return fakeSession{consume: blockUntilDone}, nil
	}

	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan error, 1)
	go func() { done <- superviseConsumer(ctx, discardLogger(), connect, nil, time.Second) }()

	require.Eventually(t, func() bool { return atomic.LoadInt32(&connects) == 1 }, 2*time.Second, 2*time.Millisecond)
	cancel()

	select {
	case err := <-done:
		require.NoError(t, err)
	case <-time.After(2 * time.Second):
		t.Fatal("superviseConsumer não retornou após o cancelamento")
	}
	require.Equal(t, int32(1), atomic.LoadInt32(&connects), "não deve reconectar quando a queda é o próprio cancelamento")
}
