CREATE TABLE customers (
    id uuid PRIMARY KEY,
    name text NOT NULL,
    document_number text NOT NULL,
    investor_profile text NOT NULL,
    status text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE investment_accounts (
    id uuid PRIMARY KEY,
    customer_id uuid NOT NULL REFERENCES customers(id),
    account_number text NOT NULL,
    status text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE financial_products (
    id uuid PRIMARY KEY,
    product_type text NOT NULL,
    name text NOT NULL,
    ticker text,
    risk_level int NOT NULL,
    liquidity_days int NOT NULL,
    is_active boolean NOT NULL DEFAULT true
);

CREATE TABLE operations (
    id uuid PRIMARY KEY,
    external_id text NOT NULL,
    account_id uuid NOT NULL REFERENCES investment_accounts(id),
    product_id uuid NOT NULL REFERENCES financial_products(id),
    operation_type text NOT NULL,
    quantity numeric(18,6) NOT NULL,
    unit_price numeric(18,6) NOT NULL,
    gross_value numeric(18,2) NOT NULL,
    net_value numeric(18,2) NOT NULL,
    operation_date date NOT NULL,
    settlement_date date NOT NULL,
    status text NOT NULL,
    rejection_reason text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE portfolio_positions (
    id uuid PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES investment_accounts(id),
    product_id uuid NOT NULL REFERENCES financial_products(id),
    quantity numeric(18,6) NOT NULL,
    average_price numeric(18,6) NOT NULL,
    gross_amount numeric(18,2) NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE processing_events (
    id uuid PRIMARY KEY,
    operation_id uuid NOT NULL REFERENCES operations(id),
    event_type text NOT NULL,
    payload jsonb,
    status text NOT NULL,
    attempt_count int NOT NULL DEFAULT 0,
    error_message text,
    created_at timestamptz NOT NULL DEFAULT now(),
    processed_at timestamptz
);

CREATE TABLE audit_logs (
    id uuid PRIMARY KEY,
    entity_name text NOT NULL,
    entity_id text NOT NULL,
    action text NOT NULL,
    old_value jsonb,
    new_value jsonb,
    user_id text,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE outbox_messages (
    id uuid PRIMARY KEY,
    event_type text NOT NULL,
    version int NOT NULL,
    payload jsonb NOT NULL,
    status text NOT NULL,
    attempts int NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    published_at timestamptz,
    last_error text
);

CREATE TABLE reconciliation_imports (
    id uuid PRIMARY KEY,
    source text NOT NULL,
    reference_date date NOT NULL,
    status text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE reconciliation_divergences (
    id uuid PRIMARY KEY,
    reconciliation_import_id uuid NOT NULL REFERENCES reconciliation_imports(id),
    account_id uuid NOT NULL REFERENCES investment_accounts(id),
    product_id uuid NOT NULL REFERENCES financial_products(id),
    internal_quantity numeric(18,6) NOT NULL,
    custodian_quantity numeric(18,6) NOT NULL,
    status text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    resolved_at timestamptz
);

CREATE UNIQUE INDEX ux_operations_external_id ON operations(external_id);
CREATE INDEX idx_operations_status ON operations(status);
CREATE INDEX idx_operations_operation_date ON operations(operation_date);
CREATE INDEX idx_operations_account_product ON operations(account_id, product_id);
CREATE INDEX idx_positions_account_product ON portfolio_positions(account_id, product_id);
CREATE INDEX idx_processing_events_status_created_at ON processing_events(status, created_at);
CREATE INDEX idx_outbox_messages_status_created_at ON outbox_messages(status, created_at);
