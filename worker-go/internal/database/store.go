package database

import (
	"context"
	"errors"

	"investops/worker/internal/operations"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"
)

type Store struct {
	pool *pgxpool.Pool
}

func NewStore(pool *pgxpool.Pool) *Store {
	return &Store{pool: pool}
}

func (s *Store) GetOperation(ctx context.Context, id uuid.UUID) (operations.Operation, bool, error) {
	const q = `
		SELECT id, external_id, account_id, product_id, operation_type, quantity, unit_price, status
		FROM operations WHERE id = $1;`
	var op operations.Operation
	err := s.pool.QueryRow(ctx, q, id).Scan(
		&op.ID, &op.ExternalID, &op.AccountID, &op.ProductID, &op.Type, &op.Quantity, &op.UnitPrice, &op.Status)
	if errors.Is(err, pgx.ErrNoRows) {
		return operations.Operation{}, false, nil
	}
	if err != nil {
		return operations.Operation{}, false, err
	}
	return op, true, nil
}

func (s *Store) GetCustomerProfileByAccount(ctx context.Context, accountID uuid.UUID) (string, error) {
	const q = `
		SELECT c.investor_profile
		FROM investment_accounts a
		JOIN customers c ON c.id = a.customer_id
		WHERE a.id = $1;`
	var profile string
	err := s.pool.QueryRow(ctx, q, accountID).Scan(&profile)
	return profile, err
}

func (s *Store) GetProductRiskLevel(ctx context.Context, productID uuid.UUID) (int, error) {
	const q = `SELECT risk_level FROM financial_products WHERE id = $1;`
	var riskLevel int
	err := s.pool.QueryRow(ctx, q, productID).Scan(&riskLevel)
	return riskLevel, err
}

func (s *Store) WithinTx(ctx context.Context, fn func(operations.TxRepo) error) error {
	tx, err := s.pool.BeginTx(ctx, pgx.TxOptions{})
	if err != nil {
		return err
	}
	defer func() { _ = tx.Rollback(ctx) }() // no-op se já houve commit

	if err := fn(&txRepo{tx: tx}); err != nil {
		return err
	}
	return tx.Commit(ctx)
}

type txRepo struct {
	tx pgx.Tx
}

func (r *txRepo) GetPositionForUpdate(ctx context.Context, accountID, productID uuid.UUID) (operations.Position, bool, error) {
	const q = `SELECT quantity, average_price FROM portfolio_positions WHERE account_id = $1 AND product_id = $2 FOR UPDATE;`
	var p operations.Position
	err := r.tx.QueryRow(ctx, q, accountID, productID).Scan(&p.Quantity, &p.AveragePrice)
	if errors.Is(err, pgx.ErrNoRows) {
		return operations.Position{}, false, nil
	}
	if err != nil {
		return operations.Position{}, false, err
	}
	return p, true, nil
}

func (r *txRepo) UpsertPosition(ctx context.Context, accountID, productID uuid.UUID, p operations.Position) error {
	const q = `
		INSERT INTO portfolio_positions (id, account_id, product_id, quantity, average_price, gross_amount, updated_at)
		VALUES ($1, $2, $3, $4, $5, $6, now())
		ON CONFLICT (account_id, product_id) DO UPDATE
		SET quantity = EXCLUDED.quantity,
		    average_price = EXCLUDED.average_price,
		    gross_amount = EXCLUDED.gross_amount,
		    updated_at = now();`
	_, err := r.tx.Exec(ctx, q, uuid.New(), accountID, productID, p.Quantity, p.AveragePrice, p.GrossAmount())
	return err
}

func (r *txRepo) UpdateOperationStatus(ctx context.Context, id uuid.UUID, status, reason string) error {
	const q = `UPDATE operations SET status = $2, rejection_reason = $3, updated_at = now() WHERE id = $1;`
	var reasonArg any
	if reason != "" {
		reasonArg = reason
	}
	_, err := r.tx.Exec(ctx, q, id, status, reasonArg)
	return err
}

func (r *txRepo) MarkOperationFailed(ctx context.Context, id uuid.UUID) error {
	const q = `UPDATE operations SET status = 'Failed', updated_at = now()
	           WHERE id = $1 AND status NOT IN ('Processed', 'Rejected');`
	_, err := r.tx.Exec(ctx, q, id)
	return err
}

func (r *txRepo) InsertProcessingEvent(ctx context.Context, e operations.ProcessingEvent) error {
	const q = `
		INSERT INTO processing_events (id, operation_id, event_type, payload, status, attempt_count, error_message, created_at, processed_at)
		VALUES ($1, $2, $3, $4::jsonb, $5, $6, $7, now(), now());`
	var errArg any
	if e.ErrorMessage != "" {
		errArg = e.ErrorMessage
	}
	_, err := r.tx.Exec(ctx, q, uuid.New(), e.OperationID, e.EventType, e.Payload, e.Status, e.AttemptCount, errArg)
	return err
}

func (r *txRepo) InsertOutbox(ctx context.Context, m operations.OutboxMessage) error {
	const q = `
		INSERT INTO outbox_messages (id, event_type, version, payload, status, attempts, created_at, source)
		VALUES ($1, $2, $3, $4::jsonb, 'Pending', 0, now(), 'worker');`
	_, err := r.tx.Exec(ctx, q, uuid.New(), m.EventType, m.Version, m.Payload)
	return err
}
