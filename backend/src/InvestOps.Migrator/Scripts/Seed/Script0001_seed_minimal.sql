INSERT INTO financial_products (id, product_type, name, ticker, risk_level, liquidity_days, is_active) VALUES
('11111111-1111-1111-1111-111111111111', 'FixedIncome', 'Tesouro Selic 2029', 'LFT',    1, 1, true),
('22222222-2222-2222-2222-222222222222', 'Fund',        'Fundo Multimercado',  'MULTI',  3, 5, true),
('33333333-3333-3333-3333-333333333333', 'Stock',       'Petrobras PN',        'PETR4',  4, 2, true),
('44444444-4444-4444-4444-444444444444', 'Derivative',  'Mini Índice Futuro',  'WINFUT', 5, 1, true);

INSERT INTO customers (id, name, document_number, investor_profile, status, created_at) VALUES
('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Ana Conservadora', '11111111111', 'Conservative', 'Active', now()),
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'Bruno Agressivo',  '22222222222', 'Aggressive',   'Active', now());

INSERT INTO investment_accounts (id, customer_id, account_number, status, created_at) VALUES
('cccccccc-cccc-cccc-cccc-cccccccccccc', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'ACC-0001', 'Active', now()),
('dddddddd-dddd-dddd-dddd-dddddddddddd', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'ACC-0002', 'Active', now());
