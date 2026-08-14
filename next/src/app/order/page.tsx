"use client";

import Link from "next/link";
import { useStore } from "@/app/components/store-provider";

function formatPrice(price: number) {
  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
    maximumFractionDigits: 0,
  }).format(price);
}

function readableStatus(status: string) {
  return status.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en-PH", {
    day: "numeric",
    month: "short",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
  }).format(new Date(value));
}

export default function OrderPage() {
  const { orders, ordersError, ordersState, refreshOrders, visitorEmail } = useStore();

  return (
    <section className="page-section orders-page" aria-labelledby="orders-heading">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Purchase history</p>
          <h1 id="orders-heading">Your orders</h1>
        </div>
        <p>Tracking orders for <span>{visitorEmail}</span></p>
      </div>

      {ordersState === "loading" && <div className="page-status" role="status">Loading your orders…</div>}

      {ordersState === "error" && (
        <div className="page-status page-status-error" role="alert">
          <p>We couldn’t load your orders. {ordersError}</p>
          <button className="text-button" type="button" onClick={() => void refreshOrders()}>
            Try again
          </button>
        </div>
      )}

      {ordersState === "ready" && orders.length === 0 && (
        <div className="empty-state">
          <p className="eyebrow">No orders yet</p>
          <h2>When you check out, your order will appear here.</h2>
          <p>Browse the catalogue and add something that fits your day.</p>
          <Link className="primary-link" href="/">Explore products</Link>
        </div>
      )}

      {ordersState === "ready" && orders.length > 0 && (
        <div className="orders-list">
          {orders.map((order, index) => (
            <article className="order-card" key={order.id}>
              <div className="order-card-heading">
                <div>
                  <p className="order-number">Order #{order.id}</p>
                  <p className="order-date">{formatDate(order.createdAtUtc)}</p>
                </div>
                <div className="order-heading-right">
                  {index === 0 && <span className="latest-order-label">Latest</span>}
                  <span className="status-pill">{readableStatus(order.orderStatus)}</span>
                </div>
              </div>

              <ul className="order-line-items">
                {order.items.map((item) => (
                  <li key={`${order.id}-${item.productId}`}>
                    <span>{item.productName} <small>× {item.quantity}</small></span>
                    <strong>{formatPrice(item.subtotal)}</strong>
                  </li>
                ))}
              </ul>

              <div className="order-card-footer">
                <div className="tracking-status">
                  <span>Payment: {readableStatus(order.paymentStatus)}</span>
                  <span>Delivery: {readableStatus(order.deliveryStatus)}</span>
                </div>
                <div className="order-total">
                  <span>Total</span>
                  <strong>{formatPrice(order.totalAmount)}</strong>
                </div>
              </div>
            </article>
          ))}
        </div>
      )}
    </section>
  );
}
