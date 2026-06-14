package main

import (
	"log/slog"
	"net"
	"os"
	"os/signal"
	"syscall"
	"time"

	pb "investops/marketdata/internal/gen/marketdatav1"
	"investops/marketdata/internal/grpcsvc"
	"investops/marketdata/internal/pricing"

	"google.golang.org/grpc"
)

func main() {
	logger := slog.New(slog.NewJSONHandler(os.Stdout, nil))

	port := os.Getenv("GRPC_PORT")
	if port == "" {
		port = "5300"
	}

	lis, err := net.Listen("tcp", ":"+port)
	if err != nil {
		logger.Error("falha ao escutar", "port", port, "error", err)
		os.Exit(1)
	}

	server := grpc.NewServer()
	pb.RegisterMarketDataServer(server, grpcsvc.New(pricing.New(), time.Second))

	go func() {
		stop := make(chan os.Signal, 1)
		signal.Notify(stop, syscall.SIGINT, syscall.SIGTERM)
		<-stop
		logger.Info("encerrando marketdata")
		server.GracefulStop()
	}()

	logger.Info("marketdata iniciado", "port", port)
	if err := server.Serve(lis); err != nil {
		logger.Error("servidor encerrou com erro", "error", err)
		os.Exit(1)
	}
}
