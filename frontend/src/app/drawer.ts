import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DashboardAction } from './api/api';
import { OrderStatus } from './api/schemas';
import { BoardStore } from './board-store';
import { MoneyPipe } from './money-pipe';

/**
 * The action buttons are display sugar derived from the restaurant slice of
 * the transition table. The server remains the authority: a rejected
 * transition simply re-syncs the board.
 */
const RESTAURANT_ACTIONS: Partial<Record<OrderStatus, { action: DashboardAction; label: string; danger?: boolean }[]>> = {
  paid: [
    { action: 'accept', label: 'Accept' },
    { action: 'reject', label: 'Reject', danger: true },
  ],
  accepted: [{ action: 'start-preparing', label: 'Start preparing' }],
  preparing: [{ action: 'mark-ready', label: 'Mark ready' }],
  ready: [{ action: 'complete', label: 'Complete' }],
};

@Component({
  selector: 'order-drawer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, FormsModule, MoneyPipe],
  template: `
    @if (store.selectedOrderId(); as orderId) {
      <aside class="drawer">
        <header class="drawer__header">
          <h2>Order #{{ orderId.slice(0, 8) }}</h2>
          <button type="button" class="drawer__close" (click)="store.select(null)">×</button>
        </header>

        @if (store.selectedOrder(); as order) {
          <div class="drawer__meta">
            <span class="drawer__status" [attr.data-status]="order.status">{{ order.status }}</span>
            <span class="drawer__customer">{{ order.customerId }}</span>
          </div>

          <section class="drawer__section">
            <h3>Items</h3>
            <ul class="lines">
              @for (line of order.lines; track $index) {
                <li class="line">
                  <div class="line__row">
                    <span>{{ line.quantity }} × {{ line.name }}</span>
                    <span>{{ line.lineTotal | money }}</span>
                  </div>
                  <div class="line__row line__row--muted">
                    <span>unit {{ line.unitPrice | money }}</span>
                  </div>
                  @for (modifier of line.modifiers; track modifier.modifierId) {
                    <div class="line__row line__row--muted">
                      <span>+ {{ modifier.name }}</span>
                      <span>{{ modifier.priceDelta | money }}</span>
                    </div>
                  }
                </li>
              }
            </ul>
            <div class="drawer__total">
              <span>Total (locked)</span>
              <strong>{{ order.total | money }}</strong>
            </div>
          </section>

          @if (order.refundAttempts > 0 || order.lastRefundError) {
            <section class="drawer__section drawer__section--refund" [class.drawer__section--alert]="order.manualInterventionRequired">
              <h3>Refund</h3>
              <p>attempts: {{ order.refundAttempts }}</p>
              @if (order.lastRefundError) {
                <p class="refund__error">{{ order.lastRefundError }}</p>
              }
              @if (order.manualInterventionRequired) {
                <p class="refund__flag">manual intervention required</p>
              }
            </section>
          }

          @if (actions(); as available) {
            @if (available.length > 0) {
              <section class="drawer__section">
                <h3>Actions</h3>
                @if (order.status === 'paid') {
                  <input
                    class="drawer__reason"
                    type="text"
                    placeholder="Rejection reason (optional)"
                    [(ngModel)]="rejectReason" />
                }
                <div class="drawer__actions">
                  @for (item of available; track item.action) {
                    <button
                      type="button"
                      class="action"
                      [class.action--danger]="item.danger"
                      (click)="run(orderId, item.action)">
                      {{ item.label }}
                    </button>
                  }
                </div>
              </section>
            }
          }

          <section class="drawer__section">
            <h3>History</h3>
            <ol class="timeline">
              @for (entry of order.history; track $index) {
                <li class="timeline__entry">
                  <span class="timeline__transition">{{ entry.from ?? '∅' }} → {{ entry.to }}</span>
                  <span class="timeline__actor" [attr.data-actor]="entry.actor">{{ entry.actor }}</span>
                  <span class="timeline__at">{{ entry.at | date: 'HH:mm:ss' }}</span>
                  @if (entry.reason) {
                    <span class="timeline__reason">{{ entry.reason }}</span>
                  }
                </li>
              }
            </ol>
          </section>
        } @else {
          <p class="drawer__loading">Waiting for projection…</p>
        }
      </aside>
    }
  `,
})
export class OrderDrawer {
  protected readonly store = inject(BoardStore);
  protected rejectReason = signal('');

  protected readonly actions = computed(() => {
    const order = this.store.selectedOrder();
    return order ? RESTAURANT_ACTIONS[order.status] ?? [] : [];
  });

  protected run(orderId: string, action: DashboardAction): void {
    const reason = action === 'reject' ? this.rejectReason() || undefined : undefined;
    this.rejectReason.set('');
    void this.store.act(orderId, action, reason);
  }
}
