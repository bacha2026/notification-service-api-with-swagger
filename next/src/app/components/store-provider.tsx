"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import type { Cart, Order, Product } from "@/app/lib/store-types";

const VISITOR_EMAIL = "visitor@example.test";
const API_PREFIX = "/api/notification";

type LoadState = "loading" | "ready" | "error";

type StoreContextValue = {
  visitorEmail: string;
  products: Product[];
  productsState: LoadState;
  productsError: string | null;
  refreshProducts: () => Promise<void>;
  cart: Cart | null;
  cartState: LoadState;
  cartError: string | null;
  refreshCart: () => Promise<void>;
  addToCart: (productId: number) => Promise<boolean>;
  updateCartItem: (cartItemId: number, quantity: number) => Promise<boolean>;
  removeCartItem: (cartItemId: number) => Promise<boolean>;
  isCartMutating: boolean;
  orders: Order[];
  ordersState: LoadState;
  ordersError: string | null;
  refreshOrders: () => Promise<void>;
  checkout: () => Promise<Order | null>;
  isCheckingOut: boolean;
  cartItemCount: number;
};

const StoreContext = createContext<StoreContextValue | null>(null);

function formatApiError(payload: unknown, fallback: string) {
  if (payload && typeof payload === "object") {
    const problem = payload as { detail?: string; title?: string };
    return problem.detail || problem.title || fallback;
  }

  return fallback;
}

async function apiRequest<T>(path: string, method = "GET", body?: unknown): Promise<T> {
  const response = await fetch(`${API_PREFIX}${path}`, {
    method,
    headers: body ? { "Content-Type": "application/json" } : undefined,
    body: body ? JSON.stringify(body) : undefined,
    cache: "no-store",
  });

  if (!response.ok) {
    let payload: unknown;

    try {
      payload = await response.json();
    } catch {
      // The API can legitimately return an empty error response.
    }

    throw new Error(formatApiError(payload, `The request could not be completed (${response.status}).`));
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

function emptyCart(): Cart {
  return {
    visitorEmail: VISITOR_EMAIL,
    items: [],
    totalAmount: 0,
  };
}

export function StoreProvider({ children }: { children: ReactNode }) {
  const [products, setProducts] = useState<Product[]>([]);
  const [productsState, setProductsState] = useState<LoadState>("loading");
  const [productsError, setProductsError] = useState<string | null>(null);
  const [cart, setCart] = useState<Cart | null>(null);
  const [cartState, setCartState] = useState<LoadState>("loading");
  const [cartError, setCartError] = useState<string | null>(null);
  const [orders, setOrders] = useState<Order[]>([]);
  const [ordersState, setOrdersState] = useState<LoadState>("loading");
  const [ordersError, setOrdersError] = useState<string | null>(null);
  const [isCartMutating, setIsCartMutating] = useState(false);
  const [isCheckingOut, setIsCheckingOut] = useState(false);

  const refreshProducts = useCallback(async () => {
    setProductsState("loading");
    setProductsError(null);

    try {
      const catalog = await apiRequest<Product[]>("/products");
      setProducts(catalog);
      setProductsState("ready");
    } catch (error) {
      setProductsState("error");
      setProductsError(error instanceof Error ? error.message : "We could not load the product catalog.");
    }
  }, []);

  const refreshCart = useCallback(async () => {
    setCartState("loading");
    setCartError(null);

    try {
      const currentCart = await apiRequest<Cart>(`/cart/${encodeURIComponent(VISITOR_EMAIL)}`);
      setCart(currentCart);
      setCartState("ready");
    } catch (error) {
      setCartState("error");
      setCartError(error instanceof Error ? error.message : "We could not load your cart.");
    }
  }, []);

  const refreshOrders = useCallback(async () => {
    setOrdersState("loading");
    setOrdersError(null);

    try {
      const visitorOrders = await apiRequest<Order[]>(
        `/orders?visitorEmail=${encodeURIComponent(VISITOR_EMAIL)}`,
      );
      setOrders(visitorOrders);
      setOrdersState("ready");
    } catch (error) {
      setOrdersState("error");
      setOrdersError(error instanceof Error ? error.message : "We could not load your orders.");
    }
  }, []);

  useEffect(() => {
    let isCurrent = true;

    async function loadInitialStore() {
      const [catalogResult, cartResult, ordersResult] = await Promise.allSettled([
        apiRequest<Product[]>("/products"),
        apiRequest<Cart>(`/cart/${encodeURIComponent(VISITOR_EMAIL)}`),
        apiRequest<Order[]>(`/orders?visitorEmail=${encodeURIComponent(VISITOR_EMAIL)}`),
      ]);

      if (!isCurrent) return;

      if (catalogResult.status === "fulfilled") {
        setProducts(catalogResult.value);
        setProductsState("ready");
      } else {
        setProductsState("error");
        setProductsError(
          catalogResult.reason instanceof Error ? catalogResult.reason.message : "We could not load the product catalog.",
        );
      }

      if (cartResult.status === "fulfilled") {
        setCart(cartResult.value);
        setCartState("ready");
      } else {
        setCartState("error");
        setCartError(cartResult.reason instanceof Error ? cartResult.reason.message : "We could not load your cart.");
      }

      if (ordersResult.status === "fulfilled") {
        setOrders(ordersResult.value);
        setOrdersState("ready");
      } else {
        setOrdersState("error");
        setOrdersError(ordersResult.reason instanceof Error ? ordersResult.reason.message : "We could not load your orders.");
      }
    }

    void loadInitialStore();

    return () => {
      isCurrent = false;
    };
  }, []);

  const addToCart = useCallback(async (productId: number) => {
    setIsCartMutating(true);
    setCartError(null);

    try {
      const updatedCart = await apiRequest<Cart>("/cart/items", "POST", {
        visitorEmail: VISITOR_EMAIL,
        productId,
        quantity: 1,
      });
      setCart(updatedCart);
      setCartState("ready");
      return true;
    } catch (error) {
      setCartError(error instanceof Error ? error.message : "We could not add this item to your cart.");
      return false;
    } finally {
      setIsCartMutating(false);
    }
  }, []);

  const removeCartItem = useCallback(
    async (cartItemId: number) => {
      setIsCartMutating(true);
      setCartError(null);

      try {
        await apiRequest<void>(`/cart/items/${cartItemId}`, "DELETE");
        await refreshCart();
        return true;
      } catch (error) {
        setCartError(error instanceof Error ? error.message : "We could not remove this item.");
        return false;
      } finally {
        setIsCartMutating(false);
      }
    },
    [refreshCart],
  );

  const updateCartItem = useCallback(
    async (cartItemId: number, quantity: number) => {
      if (quantity < 1) {
        return removeCartItem(cartItemId);
      }

      setIsCartMutating(true);
      setCartError(null);

      try {
        const updatedCart = await apiRequest<Cart>(`/cart/items/${cartItemId}`, "PUT", { quantity });
        setCart(updatedCart);
        setCartState("ready");
        return true;
      } catch (error) {
        setCartError(error instanceof Error ? error.message : "We could not update this item.");
        return false;
      } finally {
        setIsCartMutating(false);
      }
    },
    [removeCartItem],
  );

  const checkout = useCallback(async () => {
    setIsCheckingOut(true);
    setCartError(null);

    try {
      const placedOrder = await apiRequest<Order>("/orders", "POST", {
        visitorEmail: VISITOR_EMAIL,
      });
      setCart(emptyCart());
      setCartState("ready");
      setOrders((current) => [placedOrder, ...current.filter((order) => order.id !== placedOrder.id)]);
      setOrdersState("ready");
      return placedOrder;
    } catch (error) {
      setCartError(error instanceof Error ? error.message : "We could not place your order.");
      return null;
    } finally {
      setIsCheckingOut(false);
    }
  }, []);

  const value = useMemo<StoreContextValue>(
    () => ({
      visitorEmail: VISITOR_EMAIL,
      products,
      productsState,
      productsError,
      refreshProducts,
      cart,
      cartState,
      cartError,
      refreshCart,
      addToCart,
      updateCartItem,
      removeCartItem,
      isCartMutating,
      orders,
      ordersState,
      ordersError,
      refreshOrders,
      checkout,
      isCheckingOut,
      cartItemCount: cart?.items.reduce((total, item) => total + item.quantity, 0) ?? 0,
    }),
    [
      addToCart,
      cart,
      cartError,
      cartState,
      checkout,
      isCartMutating,
      isCheckingOut,
      orders,
      ordersError,
      ordersState,
      products,
      productsError,
      productsState,
      refreshCart,
      refreshOrders,
      refreshProducts,
      removeCartItem,
      updateCartItem,
    ],
  );

  return <StoreContext.Provider value={value}>{children}</StoreContext.Provider>;
}

export function useStore() {
  const store = useContext(StoreContext);

  if (!store) {
    throw new Error("useStore must be used inside StoreProvider.");
  }

  return store;
}
