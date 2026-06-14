package grpcsvc

import (
	"context"
	"net"
	"testing"
	"time"

	pb "investops/marketdata/internal/gen/marketdatav1"
	"investops/marketdata/internal/pricing"

	"github.com/shopspring/decimal"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials/insecure"
	"google.golang.org/grpc/test/bufconn"
)

func dialBufnet(t *testing.T) pb.MarketDataClient {
	t.Helper()
	lis := bufconn.Listen(1024 * 1024)
	srv := grpc.NewServer()
	pb.RegisterMarketDataServer(srv, New(pricing.New(), 10*time.Millisecond))
	go func() { _ = srv.Serve(lis) }()
	t.Cleanup(srv.Stop)

	conn, err := grpc.NewClient(
		"passthrough:///bufnet",
		grpc.WithContextDialer(func(ctx context.Context, _ string) (net.Conn, error) { return lis.DialContext(ctx) }),
		grpc.WithTransportCredentials(insecure.NewCredentials()),
	)
	require.NoError(t, err)
	t.Cleanup(func() { _ = conn.Close() })
	return pb.NewMarketDataClient(conn)
}

func TestGetQuoteReturnsPositivePrice(t *testing.T) {
	client := dialBufnet(t)

	quote, err := client.GetQuote(context.Background(), &pb.GetQuoteRequest{ProductId: "prod-1"})
	require.NoError(t, err)
	assert.Equal(t, "prod-1", quote.GetProductId())
	price, err := decimal.NewFromString(quote.GetPrice())
	require.NoError(t, err)
	assert.True(t, price.GreaterThan(decimal.Zero))
	assert.NotNil(t, quote.GetAsOf())
}

func TestStreamQuotesEmitsAndRespectsCancellation(t *testing.T) {
	client := dialBufnet(t)
	ctx, cancel := context.WithCancel(context.Background())

	stream, err := client.StreamQuotes(ctx, &pb.StreamQuotesRequest{ProductIds: []string{"a", "b"}})
	require.NoError(t, err)

	first, err := stream.Recv()
	require.NoError(t, err)
	assert.Contains(t, []string{"a", "b"}, first.GetProductId())

	cancel()
	// Após cancelar, o stream deve encerrar (erro de contexto cancelado em algum Recv).
	for i := 0; i < 50; i++ {
		if _, err := stream.Recv(); err != nil {
			return
		}
	}
	t.Fatal("stream não encerrou após cancelamento do contexto")
}
