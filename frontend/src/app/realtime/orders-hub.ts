import { Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { ProjectionEvent } from '../api/schemas';

/**
 * Live board updates. Events are Zod-parsed before they reach the store; a
 * payload that fails to parse is dropped and a resync is requested instead of
 * trusting partial data. On reconnect the board refetches wholesale.
 */
@Injectable({ providedIn: 'root' })
export class OrdersHub {
  private connection: HubConnection | null = null;

  async connect(
    restaurantId: string,
    onEvent: (event: ProjectionEvent) => void,
    onResync: () => void,
  ): Promise<void> {
    await this.disconnect();

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/orders')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('orderProjected', (raw: unknown) => {
      const parsed = ProjectionEvent.safeParse(raw);
      if (parsed.success) {
        onEvent(parsed.data);
      } else {
        console.warn('Dropped malformed projection event; resyncing', parsed.error);
        onResync();
      }
    });

    connection.onreconnected(async () => {
      await connection.invoke('JoinRestaurant', restaurantId);
      onResync();
    });

    await connection.start();
    await connection.invoke('JoinRestaurant', restaurantId);
    this.connection = connection;
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      const old = this.connection;
      this.connection = null;
      await old.stop().catch(() => undefined);
    }
  }
}
