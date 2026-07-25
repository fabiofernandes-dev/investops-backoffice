CREATE UNIQUE INDEX ux_financial_products_ticker
ON financial_products (ticker)
WHERE ticker IS NOT NULL;
