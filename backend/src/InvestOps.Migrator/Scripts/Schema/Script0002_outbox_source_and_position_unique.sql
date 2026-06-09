ALTER TABLE outbox_messages ADD COLUMN source text NOT NULL DEFAULT 'api';

DROP INDEX IF EXISTS idx_positions_account_product;
CREATE UNIQUE INDEX ux_positions_account_product ON portfolio_positions(account_id, product_id);
