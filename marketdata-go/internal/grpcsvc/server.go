package grpcsvc

import (
	"context"
	"time"

	pb "investops/marketdata/internal/gen/marketdatav1"
	"investops/marketdata/internal/pricing"

	"google.golang.org/protobuf/types/known/timestamppb"
)

type Server struct {
	pb.UnimplementedMarketDataServer
	engine   *pricing.Engine
	interval time.Duration
}

func New(engine *pricing.Engine, interval time.Duration) *Server {
	return &Server{engine: engine, interval: interval}
}

func (s *Server) GetQuote(_ context.Context, req *pb.GetQuoteRequest) (*pb.Quote, error) {
	return s.quote(req.GetProductId(), s.engine.Quote(req.GetProductId())), nil
}

func (s *Server) StreamQuotes(req *pb.StreamQuotesRequest, stream pb.MarketData_StreamQuotesServer) error {
	ctx := stream.Context()
	if err := s.sendTick(req.GetProductIds(), stream); err != nil {
		return err
	}
	ticker := time.NewTicker(s.interval)
	defer ticker.Stop()
	for {
		select {
		case <-ctx.Done():
			return nil
		case <-ticker.C:
			if err := s.sendTick(req.GetProductIds(), stream); err != nil {
				return err
			}
		}
	}
}

func (s *Server) sendTick(productIDs []string, stream pb.MarketData_StreamQuotesServer) error {
	for _, id := range productIDs {
		if err := stream.Send(s.quote(id, s.engine.Next(id))); err != nil {
			return err
		}
	}
	return nil
}

func (s *Server) quote(id string, price interface{ String() string }) *pb.Quote {
	return &pb.Quote{
		ProductId: id,
		Price:     price.String(),
		AsOf:      timestamppb.Now(),
	}
}
