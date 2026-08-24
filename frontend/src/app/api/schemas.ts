import { z } from 'zod';

// Runtime boundary validation: every API response and SignalR payload is
// parsed through these schemas before it touches application state. Money is
// ALWAYS a string of integer minor units on the wire and stays a string in the
// app; formatting happens only in the money pipe.

export const MoneyString = z.string().regex(/^-?\d+$/, 'money must be integer minor units as a string');
export type MoneyString = z.infer<typeof MoneyString>;

export const OrderStatus = z.enum([
  'draft',
  'paid',
  'cancelled',
  'expired',
  'accepted',
  'rejected',
  'preparing',
  'ready',
  'completed',
  'refund_pending',
  'refunded',
  'refund_failed',
]);
export type OrderStatus = z.infer<typeof OrderStatus>;

export const Actor = z.enum(['customer', 'restaurant', 'system']);

const IsoDate = z.iso.datetime({ offset: true });

export const Restaurant = z.object({
  id: z.guid(),
  name: z.string(),
  city: z.string(),
});
export type Restaurant = z.infer<typeof Restaurant>;

export const RestaurantList = z.array(Restaurant);

export const OrderLineModifier = z.object({
  modifierId: z.guid(),
  name: z.string(),
  priceDelta: MoneyString,
});

export const OrderLine = z.object({
  menuItemId: z.guid(),
  name: z.string(),
  unitPrice: MoneyString,
  quantity: z.number().int().positive(),
  lineTotal: MoneyString,
  modifiers: z.array(OrderLineModifier),
});
export type OrderLine = z.infer<typeof OrderLine>;

export const OrderSummary = z.object({
  orderId: z.guid(),
  restaurantId: z.guid(),
  customerId: z.string(),
  status: OrderStatus,
  total: MoneyString,
  createdAt: IsoDate,
  updatedAt: IsoDate,
  refundAttempts: z.number().int().nonnegative(),
  lastRefundError: z.string().nullable(),
  manualInterventionRequired: z.boolean(),
});
export type OrderSummary = z.infer<typeof OrderSummary>;

export const OrderSummaryList = z.array(OrderSummary);

export const HistoryEntry = z.object({
  from: OrderStatus.nullable(),
  to: OrderStatus,
  actor: Actor,
  at: IsoDate,
  reason: z.string().nullable(),
});
export type HistoryEntry = z.infer<typeof HistoryEntry>;

export const OrderDetails = OrderSummary.extend({
  lines: z.array(OrderLine),
  expiresAt: IsoDate,
  history: z.array(HistoryEntry),
});
export type OrderDetails = z.infer<typeof OrderDetails>;

/** What the projector broadcasts over SignalR after each committed projection. */
export const ProjectionEvent = z.object({
  eventType: z.string(),
  order: OrderSummary,
  historyDelta: HistoryEntry.nullable(),
});
export type ProjectionEvent = z.infer<typeof ProjectionEvent>;

/** Write-side command responses; the board itself resyncs from the read side. */
export const CommandOrderResponse = z.object({
  orderId: z.guid(),
  status: OrderStatus,
});
export type CommandOrderResponse = z.infer<typeof CommandOrderResponse>;
