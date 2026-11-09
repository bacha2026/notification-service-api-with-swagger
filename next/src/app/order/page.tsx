"use client";

import Link from "next/link";
import { useState } from "react";
import { useStore } from "@/app/components/store-provider";

function formatPrice(price: number) {
  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
    maximumFractionDigits: 0,
  }).format(price);
}

const orderStatusLabels: Record<number, string> = {
  1: "Draft", 2: "Pending", 3: "Awaiting Payment", 4: "Payment Verified",
  5: "Confirmed", 6: "Preparing", 7: "Ready for Pickup", 8: "Out for Delivery",
  9: "Delivered", 10: "Completed", 11: "Cancelled", 12: "Rejected", 13: "Refunded",
};
const paymentStatusLabels: Record<number, string> = {
  1: "Unpaid", 2: "Pending", 3: "Processing", 4: "Paid", 5: "Partially Paid",
  6: "Failed", 7: "Refunded", 8: "Partially Refunded", 9: "Chargeback",
};
const fulfillmentStatusLabels: Record<number, string> = {
  1: "Not Started", 2: "Picking", 3: "Packing", 4: "Packed", 5: "Ready", 6: "Assigned to Rider",
};
const deliveryStatusLabels: Record<number, string> = {
  1: "Waiting for Rider", 2: "Rider Assigned", 3: "Rider En Route to Store",
  4: "Rider Arrived at Store", 5: "Picked Up", 6: "On the Way", 7: "Near Destination",
  8: "Delivered", 9: "Delivery Failed", 10: "Returned",
};

function readableStatus(status: string | number, labels: Record<number, string>) {
  if (typeof status === "number") return labels[status] ?? "Unknown";
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
  const { orders, ordersError, ordersState, refreshOrders, visitorEmail, deleteOrder, canDeleteOrders } = useStore();
  const [deletingOrderId, setDeletingOrderId] = useState<number | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  async function handleDeleteOrder(orderId: number) {
    if (!window.confirm(`Delete order #${orderId}? This cannot be undone.`)) return;

    setDeletingOrderId(orderId);
    setDeleteError(null);
    try {
      await deleteOrder(orderId);
    } catch (error) {
      setDeleteError(error instanceof Error ? error.message : "We could not delete this order. Please try again.");
    } finally {
      setDeletingOrderId(null);
    }
  }

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
          {deleteError && <p className="page-status page-status-error" role="alert">{deleteError}</p>}
          {orders.map((order, index) => (
            <article className="order-card" key={order.id}>
              <div className="order-card-heading">
                <div>
                  <p className="order-number">Order #{order.id}</p>
                  <p className="order-date">{formatDate(order.createdAtUtc)}</p>
                </div>
                <div className="order-heading-right">
                  {index === 0 && <span className="latest-order-label">Latest</span>}
                  <span className="status-pill">{readableStatus(order.orderStatus, orderStatusLabels)}</span>
                  {canDeleteOrders && <button
                    className="text-button"
                    type="button"
                    disabled={deletingOrderId !== null}
                    onClick={() => void handleDeleteOrder(order.id)}
                    aria-label={`Delete order ${order.id}`}
                  >
                    {deletingOrderId === order.id ? "Deleting…" : "Delete order"}
                  </button>}
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
                  <span>Payment: {readableStatus(order.paymentStatus, paymentStatusLabels)}</span>
                  <span>Fulfillment: {readableStatus(order.fulfillmentStatus, fulfillmentStatusLabels)}</span>
                  <span>Delivery: {readableStatus(order.deliveryStatus, deliveryStatusLabels)}</span>
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
