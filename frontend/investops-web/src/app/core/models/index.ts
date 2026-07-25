export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface OperationListItem {
  id: string;
  externalId: string;
  accountId: string;
  productId: string;
  customerName: string;
  accountNumber: string;
  productName: string;
  ticker: string | null;
  operationType: string;
  quantity: number;
  unitPrice: number;
  grossValue: number;
  netValue: number;
  operationDate: string;
  settlementDate: string;
  status: string;
  rejectionReason: string | null;
}

export interface OperationDetail {
  id: string;
  externalId: string;
  accountId: string;
  productId: string;
  operationType: string;
  quantity: number;
  unitPrice: number;
  grossValue: number;
  netValue: number;
  operationDate: string;
  settlementDate: string;
  status: string;
  rejectionReason: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ProcessingEvent {
  eventType: string;
  status: string;
  attemptCount: number;
  errorMessage: string | null;
  createdAt: string;
  processedAt: string | null;
}

export interface AuditLog {
  action: string;
  oldValue: string | null;
  newValue: string | null;
  userId: string | null;
  createdAt: string;
}

export interface Position {
  id: string;
  accountId: string;
  productId: string;
  customerName: string;
  accountNumber: string;
  productName: string;
  ticker: string | null;
  quantity: number;
  averagePrice: number;
  grossAmount: number;
  updatedAt: string;
}

export interface DashboardSummary {
  importedToday: number;
  processed: number;
  rejected: number;
  failed: number;
  divergencesPending: number;
  averageProcessingSeconds: number | null;
}

export interface ProcessingMetrics {
  operationsByStatus: Record<string, number>;
  outboxPending: number;
}

export interface ProductLookup {
  id: string;
  name: string;
  ticker: string | null;
}

export interface Product {
  id: string;
  productType: string;
  name: string;
  ticker: string | null;
  riskLevel: number;
  liquidityDays: number;
  isActive: boolean;
}

export interface ProductRequest {
  productType: string;
  name: string;
  ticker: string | null;
  riskLevel: number;
  liquidityDays: number;
  isActive?: boolean;
}

export interface CustomerLookup {
  id: string;
  name: string;
}

export interface AccountLookup {
  id: string;
  accountNumber: string;
  customerName: string;
}

export interface Quote {
  productId: string;
  price: number;
  asOf: string;
}

export interface ImportOperationRequest {
  externalId: string;
  accountId: string;
  productId: string;
  operationType: string;
  quantity: number;
  unitPrice: number;
  operationDate: string;
}

export interface OperationsQuery {
  page: number;
  pageSize: number;
  sort?: string;
  status?: string;
  productId?: string;
  customerId?: string;
  externalId?: string;
}

export interface PositionsQuery {
  page: number;
  pageSize: number;
  sort?: string;
  accountId?: string;
  productId?: string;
  customerId?: string;
}

export interface Divergence {
  id: string;
  importId: string;
  accountId: string;
  customerName: string;
  accountNumber: string;
  productId: string;
  productName: string;
  ticker: string | null;
  internalQuantity: number;
  custodianQuantity: number;
  difference: number;
  status: string;
  createdAt: string;
  resolvedAt: string | null;
}

export interface DivergencesQuery {
  page: number;
  pageSize: number;
  sort?: string;
  status?: string;
}

export interface ProcessingEventItem {
  id: string;
  operationId: string;
  externalId: string;
  eventType: string;
  status: string;
  attemptCount: number;
  errorMessage: string | null;
  createdAt: string;
  processedAt: string | null;
}

export interface ProcessingEventsQuery {
  page: number;
  pageSize: number;
  sort?: string;
  status?: string;
}

export interface ImportReconciliationResult {
  importId: string;
  source: string;
  referenceDate: string;
  rowsProcessed: number;
  matchedCount: number;
  divergenceCount: number;
}
