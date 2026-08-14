"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useStore } from "@/app/components/store-provider";

function formatPrice(price: number) {
  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
    maximumFractionDigits: 0,
  }).format(price);
}

function MinusIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
      <path d="M6 12h12" strokeLinecap="round" />
    </svg>
  );
}

function PlusIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
      <path d="M12 6v12M6 12h12" strokeLinecap="round" />
    </svg>
  );
}

export default function CartPage() {
  const router = useRouter();
  const {
    cart,
    cartError,
    cartState,
    checkout,
    isCartMutating,
    isCheckingOut,
    refreshCart,
    removeCartItem,
    updateCartItem,
    visitorEmail,
  } = useStore();
  const items = cart?.items ?? [];

  async function handleCheckout() {
    const order = await checkout();

    if (order) {
      router.push("/order");
    }
  }

  return (
    <section className="page-section cart-page" aria-labelledby="cart-heading">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Your Urban Grit picks</p>
          <h1 id="cart-heading">Your cart</h1>
        </div>
        <p>Saved for <span>{visitorEmail}</span></p>
      </div>

      {cartState === "loading" && <div className="page-status" role="status">Loading your cart…</div>}

      {cartState === "error" && (
        <div className="page-status page-status-error" role="alert">
          <p>We couldn’t load your cart. {cartError}</p>
          <button className="text-button" type="button" onClick={() => void refreshCart()}>
            Try again
          </button>
        </div>
      )}

      {cartState === "ready" && items.length === 0 && (
        <div className="empty-state">
          <p className="eyebrow">Nothing here yet</p>
          <h2>Your cart is ready when you are.</h2>
          <p>Add a few favorites from the catalogue, then come back here to check out.</p>
          <Link className="primary-link" href="/">Browse products</Link>
        </div>
      )}

      {cartState === "ready" && items.length > 0 && (
        <div className="cart-layout">
          <div className="cart-items" aria-live="polite">
            {items.map((item) => (
              <article className="cart-item" key={item.id}>
                <div
                  className="cart-item-image"
                  role="img"
                  aria-label={item.productName}
                  style={{ backgroundImage: `url("${item.imageUrl}")` }}
                />
                <div className="cart-item-details">
                  <div>
                    <h2>{item.productName}</h2>
                    <p>{formatPrice(item.unitPrice)} each</p>
                  </div>
                  <button
                    className="remove-button"
                    type="button"
                    disabled={isCartMutating || isCheckingOut}
                    onClick={() => void removeCartItem(item.id)}
                  >
                    Remove
                  </button>
                </div>
                <div className="cart-item-actions">
                  <div className="quantity-control" aria-label={`Quantity for ${item.productName}`}>
                    <button
                      type="button"
                      aria-label={`Decrease ${item.productName} quantity`}
                      disabled={isCartMutating || isCheckingOut}
                      onClick={() => void updateCartItem(item.id, item.quantity - 1)}
                    >
                      <MinusIcon />
                    </button>
                    <output>{item.quantity}</output>
                    <button
                      type="button"
                      aria-label={`Increase ${item.productName} quantity`}
                      disabled={isCartMutating || isCheckingOut}
                      onClick={() => void updateCartItem(item.id, item.quantity + 1)}
                    >
                      <PlusIcon />
                    </button>
                  </div>
                  <strong>{formatPrice(item.subtotal)}</strong>
                </div>
              </article>
            ))}
          </div>

          <aside className="order-summary" aria-label="Order summary">
            <p className="eyebrow">Order summary</p>
            <div className="summary-row">
              <span>Items ({items.reduce((total, item) => total + item.quantity, 0)})</span>
              <span>{formatPrice(cart?.totalAmount ?? 0)}</span>
            </div>
            <div className="summary-row">
              <span>Delivery</span>
              <span>Free</span>
            </div>
            <div className="summary-total">
              <span>Total</span>
              <strong>{formatPrice(cart?.totalAmount ?? 0)}</strong>
            </div>
            {cartError && <p className="inline-error" role="alert">{cartError}</p>}
            <button
              className="checkout-button"
              type="button"
              disabled={isCheckingOut || isCartMutating}
              onClick={() => void handleCheckout()}
            >
              {isCheckingOut ? "Placing your order…" : "Checkout"}
            </button>
            <p className="summary-note">Checkout creates your order in Notification API and takes you to order tracking.</p>
          </aside>
        </div>
      )}
    </section>
  );
}
