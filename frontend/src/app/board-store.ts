import { Injectable, computed, inject, signal } from '@angular/core';
import { Api, DashboardAction } from './api/api';
import { OrderDetails, OrderStatus, OrderSummary, ProjectionEvent, Restaurant } from './api/schemas';
import { OrdersHub } from './realtime/orders-hub';

export interface KitchenColumn {
  key: string;
  title: string;
  status: OrderStatus;
  orders: OrderSummary[];
}

const KITCHEN_COLUMNS: ReadonlyArray<{ key: string; title: string; status: OrderStatus }> = [
  { key: 'new', title: 'New', status: 'paid' },
  { key: 'accepted', title: 'Accepted', status: 'accepted' },
  { key: 'preparing', title: 'Preparing', status: 'preparing' },
  { key: 'ready', title: 'Ready', status: 'ready' },
  { key: 'completed', title: 'Completed', status: 'completed' },
];

const PAYMENT_STATUSES: ReadonlyArray<OrderStatus> = ['refund_pending', 'refunded', 'refund_failed'];

/**
 * All board state as signals. SignalR events patch the board in place; a
 * rejected transition or a reconnect triggers a full re-sync — the server is
 * the authority and the UI never decides legality beyond choosing which
 * buttons to render.
 */
@Injectable({ providedIn: 'root' })
export class BoardStore {
  private readonly api = inject(Api);
  private readonly hub = inject(OrdersHub);

  readonly restaurants = signal<Restaurant[]>([]);
  readonly restaurantId = signal<string | null>(null);
  readonly live = signal(false);
  readonly loadError = signal<string | null>(null);

  private readonly ordersById = signal<ReadonlyMap<string, OrderSummary>>(new Map());

  readonly selectedOrderId = signal<string | null>(null);
  readonly selectedOrder = signal<OrderDetails | null>(null);

  readonly restaurant = computed(() =>
    this.restaurants().find((r) => r.id === this.restaurantId()) ?? null);

  readonly kitchenColumns = computed<KitchenColumn[]>(() => {
    const orders = [...this.ordersById().values()];
    return KITCHEN_COLUMNS.map((column) => ({
      ...column,
      orders: orders
        .filter((o) => o.status === column.status)
        .sort((a, b) => a.createdAt.localeCompare(b.createdAt)),
    }));
  });

  readonly paymentStrip = computed(() =>
    [...this.ordersById().values()]
      .filter((o) => PAYMENT_STATUSES.includes(o.status))
      .sort((a, b) => a.createdAt.localeCompare(b.createdAt)));

  readonly needsAttention = computed(() =>
    this.paymentStrip().some((o) => o.status === 'refund_failed'));

  async init(): Promise<void> {
    try {
      const restaurants = await this.api.listRestaurants();
      this.restaurants.set(restaurants);
      if (restaurants.length > 0) {
        await this.selectRestaurant(restaurants[0].id);
      }
    } catch (error) {
      this.loadError.set('Could not reach the ordering API. Is the backend running?');
      console.error(error);
    }
  }

  async selectRestaurant(restaurantId: string): Promise<void> {
    this.restaurantId.set(restaurantId);
    this.ordersById.set(new Map());
    this.selectedOrderId.set(null);
    this.selectedOrder.set(null);
    this.live.set(false);
    try {
      await this.hub.connect(
        restaurantId,
        (event) => this.applyEvent(event),
        () => void this.resync(),
      );
      this.live.set(true);
    } catch (error) {
      console.error('SignalR connection failed; the board will still load on demand', error);
    }
    await this.resync();
  }

  /** Full refetch from the read model — the recovery path for anything unexpected. */
  async resync(): Promise<void> {
    const restaurantId = this.restaurantId();
    if (!restaurantId) {
      return;
    }

    try {
      const orders = await this.api.listOrders(restaurantId);
      this.ordersById.set(new Map(orders.map((o) => [o.orderId, o])));
      this.loadError.set(null);
      await this.refreshSelected();
    } catch (error) {
      this.loadError.set('Re-sync failed; retrying on the next event.');
      console.error(error);
    }
  }

  async select(orderId: string | null): Promise<void> {
    this.selectedOrderId.set(orderId);
    this.selectedOrder.set(null);
    await this.refreshSelected();
  }

  /**
   * Sends a transition command. Success updates arrive via the projector's
   * SignalR broadcast (eventual consistency, embraced); any failure re-syncs
   * so the UI converges back onto server truth.
   */
  async act(orderId: string, action: DashboardAction, reason?: string): Promise<void> {
    const restaurantId = this.restaurantId();
    if (!restaurantId) {
      return;
    }

    try {
      await this.api.act(restaurantId, orderId, action, reason);
    } catch (error) {
      console.warn(`Transition ${action} was rejected by the server; re-syncing`, error);
      await this.resync();
    }
  }

  private applyEvent(event: ProjectionEvent): void {
    if (event.order.restaurantId !== this.restaurantId()) {
      return;
    }

    const next = new Map(this.ordersById());
    next.set(event.order.orderId, event.order);
    this.ordersById.set(next);

    if (this.selectedOrderId() === event.order.orderId) {
      void this.refreshSelected();
    }
  }

  private async refreshSelected(): Promise<void> {
    const orderId = this.selectedOrderId();
    if (!orderId) {
      return;
    }

    try {
      this.selectedOrder.set(await this.api.getOrder(orderId));
    } catch (error) {
      // Not projected yet (404) or transient — the next event retries.
      console.warn('Order details not available yet', error);
    }
  }
}
