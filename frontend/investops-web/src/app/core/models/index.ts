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

export interface CustomerLookup {
  id: string;
  name: string;
}

export interface OperationsQuery {
  page: number;
  pageSize: number;
  sort?: string;
  status?: string;
  productId?: string;
  customerId?: string;
}

export interface PositionsQuery {
  page: number;
  pageSize: number;
  sort?: string;
  accountId?: string;
  productId?: string;
  customerId?: string;
}
