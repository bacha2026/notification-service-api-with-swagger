"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { useAuthStore } from "@/app/lib/auth-store";
import type { Cart, Order, Product } from "@/app/lib/store-types";

const DEMO_VISITOR_EMAIL = "visitor@example.test";
const API_PREFIX = "/api/notification";
const EMPTY_ORDERS: Order[] = [];

type LoadState = "loading" | "ready" | "error";

type IdentityRequest = {
  signal: AbortSignal;
  isCurrent: () => boolean;
  cancel: () => void;
  finish: () => void;
};

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

function useIdentityRequestScope(identity: string): () => IdentityRequest {
  const activeIdentityRef = useRef<string | null>(identity);
  const controllersRef = useRef(new Set<AbortController>());

  useEffect(() => {
    activeIdentityRef.current = identity;
    const controllers = controllersRef.current;

    return () => {
      if (activeIdentityRef.current === identity) {
        activeIdentityRef.current = null;
      }

      for (const controller of controllers) {
        controller.abort();
      }
      controllers.clear();
    };
  }, [identity]);

  return useCallback(() => {
    const controller = new AbortController();
    const requestIdentity = identity;
    controllersRef.current.add(controller);

    if (activeIdentityRef.current !== requestIdentity) {
      controller.abort();
    }

    const finish = () => controllersRef.current.delete(controller);

    return {
      signal: controller.signal,
      isCurrent: () =>
        !controller.signal.aborted && activeIdentityRef.current === requestIdentity,
      cancel: () => {
        controller.abort();
        finish();
      },
      finish,
    };
  }, [identity]);
}

function formatApiError(payload: unknown, fallback: string) {
  if (payload && typeof payload === "object") {
    const problem = payload as { detail?: string; title?: string };
    return problem.detail || problem.title || fallback;
  }

  return fallback;
}

async function apiRequest<T>(
  path: string,
  method = "GET",
  body?: unknown,
  accessToken?: string,
  signal?: AbortSignal,
): Promise<T> {
  const headers = new Headers();

  if (body) headers.set("Content-Type", "application/json");
  if (accessToken) headers.set("Authorization", `Bearer ${accessToken}`);

  const response = await fetch(`${API_PREFIX}${path}`, {
    method,
    headers,
    body: body ? JSON.stringify(body) : undefined,
    cache: "no-store",
    signal,
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

function emptyCart(visitorEmail: string): Cart {
  return {
    visitorEmail,
    items: [],
    totalAmount: 0,
  };
}

export function StoreProvider({ children }: { children: ReactNode }) {
  const session = useAuthStore((state) => state.session);
  const visitorEmail = session?.user.email || DEMO_VISITOR_EMAIL;
  const accessToken = session?.accessToken;
  const requestIdentity = accessToken
    ? `authenticated:${accessToken}`
    : `guest:${visitorEmail}`;
  const beginIdentityRequest = useIdentityRequestScope(requestIdentity);
  const [products, setProducts] = useState<Product[]>([]);
  const [productsState, setProductsState] = useState<LoadState>("loading");
  const [productsError, setProductsError] = useState<string | null>(null);
  const [cart, setCart] = useState<Cart | null>(null);
  const [cartState, setCartState] = useState<LoadState>("loading");
  const [cartError, setCartError] = useState<string | null>(null);
  const [orders, setOrders] = useState<Order[]>([]);
  const [ordersState, setOrdersState] = useState<LoadState>("loading");
  const [ordersError, setOrdersError] = useState<string | null>(null);
  const [loadedForIdentity, setLoadedForIdentity] = useState<string | null>(null);
  const [cartMutationIdentity, setCartMutationIdentity] = useState<string | null>(null);
  const [checkoutIdentity, setCheckoutIdentity] = useState<string | null>(null);

  const refreshProducts = useCallback(async () => {
    const request = beginIdentityRequest();
    setProductsState("loading");
    setProductsError(null);

    try {
      const catalog = await apiRequest<Product[]>(
        "/products",
        "GET",
        undefined,
        accessToken,
        request.signal,
      );
      if (!request.isCurrent()) return;

      setProducts(catalog);
      setProductsState("ready");
    } catch (error) {
      if (!request.isCurrent()) return;

      setProductsState("error");
      setProductsError(error instanceof Error ? error.message : "We could not load the product catalog.");
    } finally {
      request.finish();
    }
  }, [accessToken, beginIdentityRequest]);

  const refreshCart = useCallback(async () => {
    const request = beginIdentityRequest();
    setCartState("loading");
    setCartError(null);

    try {
      const currentCart = await apiRequest<Cart>(
        accessToken ? "/auth/cart" : `/cart/${encodeURIComponent(visitorEmail)}`,
        "GET",
        undefined,
        accessToken,
        request.signal,
      );
      if (!request.isCurrent()) return;

      setCart(currentCart);
      setCartState("ready");
    } catch (error) {
      if (!request.isCurrent()) return;

      setCartState("error");
      setCartError(error instanceof Error ? error.message : "We could not load your cart.");
    } finally {
      request.finish();
    }
  }, [accessToken, beginIdentityRequest, visitorEmail]);

  const refreshOrders = useCallback(async () => {
    const request = beginIdentityRequest();
    setOrdersState("loading");
    setOrdersError(null);

    try {
      // Authenticated order history is bound to the signed JWT subject. The
      // legacy visitor-email route remains only for the existing guest flow.
      const visitorOrders = await apiRequest<Order[]>(
        accessToken ? "/auth/orders" : `/orders?visitorEmail=${encodeURIComponent(visitorEmail)}`,
        "GET",
        undefined,
        accessToken,
        request.signal,
      );
      if (!request.isCurrent()) return;

      setOrders(visitorOrders);
      setOrdersState("ready");
    } catch (error) {
      if (!request.isCurrent()) return;

      setOrdersState("error");
      setOrdersError(error instanceof Error ? error.message : "We could not load your orders.");
    } finally {
      request.finish();
    }
  }, [accessToken, beginIdentityRequest, visitorEmail]);

  useEffect(() => {
    const request = beginIdentityRequest();

    async function loadInitialStore() {
      try {
        const [catalogResult, cartResult, ordersResult] = await Promise.allSettled([
          apiRequest<Product[]>("/products", "GET", undefined, accessToken, request.signal),
          apiRequest<Cart>(
            accessToken ? "/auth/cart" : `/cart/${encodeURIComponent(visitorEmail)}`,
            "GET",
            undefined,
            accessToken,
            request.signal,
          ),
          apiRequest<Order[]>(
            accessToken ? "/auth/orders" : `/orders?visitorEmail=${encodeURIComponent(visitorEmail)}`,
            "GET",
            undefined,
            accessToken,
            request.signal,
          ),
        ]);

        if (!request.isCurrent()) return;

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

        setLoadedForIdentity(requestIdentity);
      } finally {
        request.finish();
      }
    }

    void loadInitialStore();

    return request.cancel;
  }, [accessToken, beginIdentityRequest, requestIdentity, visitorEmail]);

  // Do not expose another account's cached cart or orders while a login/logout
  // transition is retrieving data for the current request identity.
  const isCurrentVisitorData = loadedForIdentity === requestIdentity;
  const visibleCart = isCurrentVisitorData ? cart : null;
  const visibleOrders = isCurrentVisitorData ? orders : EMPTY_ORDERS;
  const visibleCartState: LoadState = isCurrentVisitorData ? cartState : "loading";
  const visibleOrdersState: LoadState = isCurrentVisitorData ? ordersState : "loading";
  const visibleCartError = isCurrentVisitorData ? cartError : null;
  const visibleOrdersError = isCurrentVisitorData ? ordersError : null;
  const isCartMutating = cartMutationIdentity === requestIdentity;
  const isCheckingOut = checkoutIdentity === requestIdentity;

  const addToCart = useCallback(async (productId: number) => {
    const request = beginIdentityRequest();
    setCartMutationIdentity(requestIdentity);
    setCartError(null);

    try {
      // The authenticated endpoint intentionally has no visitor email field;
      // the API binds the cart to the signed JWT subject.
      const updatedCart = await apiRequest<Cart>(
        accessToken ? "/auth/cart/items" : "/cart/items",
        "POST",
        accessToken ? { productId, quantity: 1 } : { visitorEmail, productId, quantity: 1 },
        accessToken,
        request.signal,
      );
      if (!request.isCurrent()) return false;

      setCart(updatedCart);
      setCartState("ready");
      return true;
    } catch (error) {
      if (!request.isCurrent()) return false;

      setCartError(error instanceof Error ? error.message : "We could not add this item to your cart.");
      return false;
    } finally {
      if (request.isCurrent()) setCartMutationIdentity(null);
      request.finish();
    }
  }, [accessToken, beginIdentityRequest, requestIdentity, visitorEmail]);

  const removeCartItem = useCallback(
    async (cartItemId: number) => {
      const request = beginIdentityRequest();
      setCartMutationIdentity(requestIdentity);
      setCartError(null);

      try {
        await apiRequest<void>(
          `${accessToken ? "/auth/cart/items" : "/cart/items"}/${cartItemId}`,
          "DELETE",
          undefined,
          accessToken,
          request.signal,
        );
        if (!request.isCurrent()) return false;

        await refreshCart();
        if (!request.isCurrent()) return false;

        return true;
      } catch (error) {
        if (!request.isCurrent()) return false;

        setCartError(error instanceof Error ? error.message : "We could not remove this item.");
        return false;
      } finally {
        if (request.isCurrent()) setCartMutationIdentity(null);
        request.finish();
      }
    },
    [accessToken, beginIdentityRequest, refreshCart, requestIdentity],
  );

  const updateCartItem = useCallback(
    async (cartItemId: number, quantity: number) => {
      if (quantity < 1) {
        return removeCartItem(cartItemId);
      }

      const request = beginIdentityRequest();
      setCartMutationIdentity(requestIdentity);
      setCartError(null);

      try {
        const updatedCart = await apiRequest<Cart>(
          `${accessToken ? "/auth/cart/items" : "/cart/items"}/${cartItemId}`,
          "PUT",
          { quantity },
          accessToken,
          request.signal,
        );
        if (!request.isCurrent()) return false;

        setCart(updatedCart);
        setCartState("ready");
        return true;
      } catch (error) {
        if (!request.isCurrent()) return false;

        setCartError(error instanceof Error ? error.message : "We could not update this item.");
        return false;
      } finally {
        if (request.isCurrent()) setCartMutationIdentity(null);
        request.finish();
      }
    },
    [accessToken, beginIdentityRequest, removeCartItem, requestIdentity],
  );

  const checkout = useCallback(async () => {
    const request = beginIdentityRequest();
    setCheckoutIdentity(requestIdentity);
    setCartError(null);

    try {
      // The authenticated route derives both owner id and visitor email from
      // the verified bearer token; it intentionally accepts no client email.
      const placedOrder = await apiRequest<Order>(
        accessToken ? "/auth/orders" : "/orders",
        "POST",
        accessToken ? undefined : { visitorEmail },
        accessToken,
        request.signal,
      );
      if (!request.isCurrent()) return null;

      setCart(emptyCart(visitorEmail));
      setCartState("ready");
      setOrders((current) => [placedOrder, ...current.filter((order) => order.id !== placedOrder.id)]);
      setOrdersState("ready");
      return placedOrder;
    } catch (error) {
      if (!request.isCurrent()) return null;

      setCartError(error instanceof Error ? error.message : "We could not place your order.");
      return null;
    } finally {
      if (request.isCurrent()) setCheckoutIdentity(null);
      request.finish();
    }
  }, [accessToken, beginIdentityRequest, requestIdentity, visitorEmail]);

  const value = useMemo<StoreContextValue>(
    () => ({
      visitorEmail,
      products,
      productsState,
      productsError,
      refreshProducts,
      cart: visibleCart,
      cartState: visibleCartState,
      cartError: visibleCartError,
      refreshCart,
      addToCart,
      updateCartItem,
      removeCartItem,
      isCartMutating,
      orders: visibleOrders,
      ordersState: visibleOrdersState,
      ordersError: visibleOrdersError,
      refreshOrders,
      checkout,
      isCheckingOut,
      cartItemCount: visibleCart?.items.reduce((total, item) => total + item.quantity, 0) ?? 0,
    }),
    [
      addToCart,
      visibleCart,
      visibleCartError,
      visibleCartState,
      checkout,
      isCartMutating,
      isCheckingOut,
      visibleOrders,
      visibleOrdersError,
      visibleOrdersState,
      products,
      productsError,
      productsState,
      refreshCart,
      refreshOrders,
      refreshProducts,
      removeCartItem,
      updateCartItem,
      visitorEmail,
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
