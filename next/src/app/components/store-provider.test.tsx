import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { StoreProvider, useStore } from "@/app/components/store-provider";
import { useAuthStore, type AuthSession } from "@/app/lib/auth-store";
import type { Cart } from "@/app/lib/store-types";

function session(id: string, email: string, accessToken: string): AuthSession {
  return {
    accessToken,
    expiresAtUtc: "2026-08-27T12:00:00.000Z",
    user: {
      id,
      firstName: id,
      lastName: "User",
      email,
      photoUrl: null,
      mfaRequired: false,
      isAdmin: false,
    },
  };
}

function cart(visitorEmail: string, totalAmount: number): Cart {
  return { visitorEmail, items: [], totalAmount };
}

function jsonResponse(payload: unknown): Response {
  return new Response(JSON.stringify(payload), {
    headers: { "Content-Type": "application/json" },
  });
}

function requestUrl(input: RequestInfo | URL): string {
  if (typeof input === "string") return input;
  return input instanceof URL ? input.toString() : input.url;
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((next) => {
    resolve = next;
  });

  return { promise, resolve };
}

function StoreProbe({ onRefresh }: { onRefresh?: (request: Promise<void>) => void }) {
  const { cart: currentCart, cartState, refreshCart, visitorEmail } = useStore();

  return (
    <>
      <output data-testid="visitor-email">{visitorEmail}</output>
      <output data-testid="cart">
        {currentCart ? `${currentCart.visitorEmail}:${currentCart.totalAmount}` : cartState}
      </output>
      <button
        type="button"
        onClick={() => {
          const request = refreshCart();
          onRefresh?.(request);
          void request;
        }}
      >
        Refresh cart
      </button>
    </>
  );
}

describe("StoreProvider", () => {
  beforeEach(() => {
    useAuthStore.setState({ session: null, isLoginDialogOpen: false });
  });

  it("aborts every initial request when the provider unmounts", async () => {
    const signals: AbortSignal[] = [];
    vi.spyOn(globalThis, "fetch").mockImplementation((_input, init) => {
      const signal = init?.signal;
      if (!(signal instanceof AbortSignal)) {
        throw new Error("The initial request did not receive an AbortSignal.");
      }

      signals.push(signal);
      return new Promise((_resolve, reject) => {
        signal.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")));
      });
    });

    const view = render(
      <StoreProvider>
        <span>child</span>
      </StoreProvider>,
    );

    await waitFor(() => expect(signals).toHaveLength(3));
    expect(signals.every((signal) => !signal.aborted)).toBe(true);

    view.unmount();

    expect(signals.every((signal) => signal.aborted)).toBe(true);
  });

  it("does not let an obsolete account refresh overwrite the current account", async () => {
    const accountA = session("account-a", "a@example.test", "token-a");
    const accountB = session("account-b", "b@example.test", "token-b");
    const staleCart = deferred<Response>();
    let accountACartCalls = 0;
    let staleSignal: AbortSignal | undefined;
    let staleRefresh: Promise<void> | undefined;

    useAuthStore.setState({ session: accountA });
    vi.spyOn(globalThis, "fetch").mockImplementation((input, init) => {
      const url = requestUrl(input);
      const authorization = new Headers(init?.headers).get("authorization");

      if (url.endsWith("/products")) return Promise.resolve(jsonResponse([]));
      if (url.endsWith("/auth/orders")) return Promise.resolve(jsonResponse([]));
      if (url.endsWith("/auth/cart") && authorization === "Bearer token-a") {
        accountACartCalls += 1;
        if (accountACartCalls === 1) {
          return Promise.resolve(jsonResponse(cart(accountA.user.email, 100)));
        }

        staleSignal = init?.signal instanceof AbortSignal ? init.signal : undefined;
        return staleCart.promise;
      }
      if (url.endsWith("/auth/cart") && authorization === "Bearer token-b") {
        return Promise.resolve(jsonResponse(cart(accountB.user.email, 200)));
      }

      throw new Error(`Unexpected request: ${url}`);
    });

    render(
      <StoreProvider>
        <StoreProbe onRefresh={(request) => { staleRefresh = request; }} />
      </StoreProvider>,
    );

    await waitFor(() => expect(screen.getByTestId("cart")).toHaveTextContent("a@example.test:100"));
    fireEvent.click(screen.getByRole("button", { name: "Refresh cart" }));
    await waitFor(() => expect(staleSignal).toBeInstanceOf(AbortSignal));

    act(() => useAuthStore.setState({ session: accountB }));

    await waitFor(() => expect(screen.getByTestId("cart")).toHaveTextContent("b@example.test:200"));
    expect(screen.getByTestId("visitor-email")).toHaveTextContent("b@example.test");
    expect(staleSignal?.aborted).toBe(true);

    await act(async () => {
      staleCart.resolve(jsonResponse(cart(accountA.user.email, 999)));
      await staleRefresh;
    });

    expect(screen.getByTestId("cart")).toHaveTextContent("b@example.test:200");
  });

  it("aborts a manual refresh when the provider unmounts", async () => {
    const account = session("account-a", "a@example.test", "token-a");
    let cartCalls = 0;
    let refreshSignal: AbortSignal | undefined;
    let refreshRequest: Promise<void> | undefined;

    useAuthStore.setState({ session: account });
    vi.spyOn(globalThis, "fetch").mockImplementation((input, init) => {
      const url = requestUrl(input);

      if (url.endsWith("/products")) return Promise.resolve(jsonResponse([]));
      if (url.endsWith("/auth/orders")) return Promise.resolve(jsonResponse([]));
      if (url.endsWith("/auth/cart")) {
        cartCalls += 1;
        if (cartCalls === 1) {
          return Promise.resolve(jsonResponse(cart(account.user.email, 100)));
        }

        refreshSignal = init?.signal instanceof AbortSignal ? init.signal : undefined;
        return new Promise((_resolve, reject) => {
          refreshSignal?.addEventListener(
            "abort",
            () => reject(new DOMException("Aborted", "AbortError")),
            { once: true },
          );
        });
      }

      throw new Error(`Unexpected request: ${url}`);
    });

    const view = render(
      <StoreProvider>
        <StoreProbe onRefresh={(request) => { refreshRequest = request; }} />
      </StoreProvider>,
    );

    await waitFor(() => expect(screen.getByTestId("cart")).toHaveTextContent("a@example.test:100"));
    fireEvent.click(screen.getByRole("button", { name: "Refresh cart" }));
    await waitFor(() => expect(refreshSignal).toBeInstanceOf(AbortSignal));

    view.unmount();
    await refreshRequest;

    expect(refreshSignal?.aborted).toBe(true);
  });
});
