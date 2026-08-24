import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  CommandOrderResponse,
  OrderDetails,
  OrderSummary,
  OrderSummaryList,
  Restaurant,
  RestaurantList,
} from './schemas';

export type DashboardAction = 'accept' | 'reject' | 'start-preparing' | 'mark-ready' | 'complete';

/** All responses are Zod-parsed at this boundary; nothing unvalidated escapes. */
@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);

  async listRestaurants(): Promise<Restaurant[]> {
    const raw = await firstValueFrom(this.http.get<unknown>('/api/restaurants'));
    return RestaurantList.parse(raw);
  }

  async listOrders(restaurantId: string): Promise<OrderSummary[]> {
    const raw = await firstValueFrom(this.http.get<unknown>(`/api/restaurants/${restaurantId}/orders`));
    return OrderSummaryList.parse(raw);
  }

  async getOrder(orderId: string): Promise<OrderDetails> {
    const raw = await firstValueFrom(this.http.get<unknown>(`/api/orders/${orderId}`));
    return OrderDetails.parse(raw);
  }

  /**
   * Dashboard transition. The server is the authority: the response is the
   * order's current state whether or not the transition applied, and callers
   * re-sync from the read model rather than trusting optimistic updates.
   */
  async act(restaurantId: string, orderId: string, action: DashboardAction, reason?: string): Promise<CommandOrderResponse> {
    const body = action === 'reject' ? { reason: reason ?? null } : {};
    const raw = await firstValueFrom(
      this.http.post<unknown>(`/api/restaurants/${restaurantId}/orders/${orderId}/${action}`, body),
    );
    return CommandOrderResponse.parse(raw);
  }
}
