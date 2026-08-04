# Week 3 React Hooks Audit — Substitute Training Set

> **Status: SUBSTITUTE TRAINING SET — EM INPUT NOT PROVIDED**
>
> The repository and the attached training plan do not contain the ten
> EM-supplied snippets named by the Week 3 assignment. The examples below were
> authored locally to exercise the same dependency, cleanup, and stale-closure
> skills. They must not be represented as EM-supplied, EM-reviewed, or
> externally published.

## How to read this artifact

Each numbered exercise contains a deliberately broken React/TypeScript example,
a corrected example, the reasoning behind the correction, and a focused
verification note. Every code block is a complete component or custom
integration boundary rather than an isolated effect body.

The examples target React 18 or later with TypeScript and browser APIs. This
repository has no React package or test runner, so the verification notes are
repeatable test designs, not claims that frontend tests were executed here.

| # | Scenario | Primary finding |
| --- | --- | --- |
| 1 | User fetch | Missing dependency and request cancellation |
| 2 | Stopwatch interval | Stale closure and missing timer cleanup |
| 3 | Viewport listener | Leaked event listener |
| 4 | Debounced search | Missing callback dependency and timer cleanup |
| 5 | Job event feed | Stale state, missing dependency, and subscription leak |
| 6 | Cart callback | Stale state captured by useCallback |
| 7 | Price calculation | Incomplete useMemo dependency list |
| 8 | Product filtering | Unnecessary effect and duplicated derived state |
| 9 | Conditional chat | Rules-of-Hooks violation and subscription lifecycle |
| 10 | Chat connection | Object dependency causes needless reconnections |

## 1. Refetch when the user changes and cancel obsolete work

### Broken TSX

~~~tsx
import { useEffect, useState } from "react";

type User = {
  id: string;
  name: string;
};

type UserCardProps = {
  userId: string;
};

export function UserCard({ userId }: UserCardProps) {
  const [user, setUser] = useState<User | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetch("/api/users/" + encodeURIComponent(userId))
      .then((response) => {
        if (!response.ok) {
          throw new Error("Request failed with " + response.status);
        }

        return response.json() as Promise<User>;
      })
      .then(setUser)
      .catch((cause: unknown) => {
        setError(cause instanceof Error ? cause.message : "Unknown error");
      });
  }, []);

  if (error) return <p role="alert">{error}</p>;
  return <p>{user ? user.name : "Loading..."}</p>;
}
~~~

### Corrected TSX

~~~tsx
import { useEffect, useState } from "react";

type User = {
  id: string;
  name: string;
};

type UserCardProps = {
  userId: string;
};

export function UserCard({ userId }: UserCardProps) {
  const [user, setUser] = useState<User | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();

    async function loadUser() {
      setError(null);

      try {
        const response = await fetch(
          "/api/users/" + encodeURIComponent(userId),
          { signal: controller.signal },
        );

        if (!response.ok) {
          throw new Error("Request failed with " + response.status);
        }

        const nextUser = (await response.json()) as User;

        if (!controller.signal.aborted) {
          setUser(nextUser);
        }
      } catch (cause: unknown) {
        if (cause instanceof DOMException && cause.name === "AbortError") {
          return;
        }

        if (!controller.signal.aborted) {
          setError(cause instanceof Error ? cause.message : "Unknown error");
        }
      }
    }

    void loadUser();
    return () => controller.abort();
  }, [userId]);

  if (error) return <p role="alert">{error}</p>;
  return <p>{user ? user.name : "Loading..."}</p>;
}
~~~

**Why:** The empty dependency list freezes the first user ID in the effect.
Adding userId reruns the request for the selected user. Aborting during cleanup
prevents an older, slower request from overwriting newer state or updating after
unmount.

**Verification note:** Mock two requests, rerender from user A to user B, and
resolve A last. Assert that A is aborted and only B is rendered. Unmount during
a pending request and assert that its signal is aborted.

## 2. Advance an interval without a stale closure

### Broken TSX

~~~tsx
import { useEffect, useState } from "react";

export function Stopwatch() {
  const [elapsedSeconds, setElapsedSeconds] = useState(0);

  useEffect(() => {
    window.setInterval(() => {
      setElapsedSeconds(elapsedSeconds + 1);
    }, 1_000);
  }, []);

  return <output>{elapsedSeconds} seconds</output>;
}
~~~

### Corrected TSX

~~~tsx
import { useEffect, useState } from "react";

export function Stopwatch() {
  const [elapsedSeconds, setElapsedSeconds] = useState(0);

  useEffect(() => {
    const intervalId = window.setInterval(() => {
      setElapsedSeconds((current) => current + 1);
    }, 1_000);

    return () => window.clearInterval(intervalId);
  }, []);

  return <output>{elapsedSeconds} seconds</output>;
}
~~~

**Why:** The broken callback always reads the initial value of elapsedSeconds,
so it repeatedly requests the value 1. A functional updater reads React's
current state without adding state to the effect dependencies. Cleanup prevents
the interval from surviving unmount.

**Verification note:** With fake timers, advance three seconds and expect the
output to be 3. Unmount, advance again, and assert that no state update or timer
callback occurs.

## 3. Remove a window event listener with the same function reference

### Broken TSX

~~~tsx
import { useEffect, useState } from "react";

export function ViewportWidth() {
  const [width, setWidth] = useState(0);

  useEffect(() => {
    window.addEventListener("resize", () => {
      setWidth(window.innerWidth);
    });
  }, []);

  return <output>{width}px</output>;
}
~~~

### Corrected TSX

~~~tsx
import { useEffect, useState } from "react";

export function ViewportWidth() {
  const [width, setWidth] = useState(0);

  useEffect(() => {
    function updateWidth() {
      setWidth(window.innerWidth);
    }

    updateWidth();
    window.addEventListener("resize", updateWidth);

    return () => {
      window.removeEventListener("resize", updateWidth);
    };
  }, []);

  return <output>{width}px</output>;
}
~~~

**Why:** The anonymous listener in the broken component cannot be removed and
continues to retain the component callback. The corrected effect uses one
function reference for registration and cleanup, and it initializes state from
the browser after mount.

**Verification note:** Spy on addEventListener and removeEventListener. Assert
that both receive the identical function reference, then dispatch resize and
confirm the displayed width changes.

## 4. Cancel superseded debounced searches

### Broken TSX

~~~tsx
import { useEffect } from "react";

type DebouncedSearchProps = {
  query: string;
  onSearch: (query: string) => void;
};

export function DebouncedSearch({
  query,
  onSearch,
}: DebouncedSearchProps) {
  useEffect(() => {
    window.setTimeout(() => {
      onSearch(query);
    }, 300);
  }, [query]);

  return <p>Search query: {query}</p>;
}
~~~

### Corrected TSX

~~~tsx
import { useEffect } from "react";

type DebouncedSearchProps = {
  query: string;
  onSearch: (query: string) => void;
};

export function DebouncedSearch({
  query,
  onSearch,
}: DebouncedSearchProps) {
  useEffect(() => {
    const timeoutId = window.setTimeout(() => {
      onSearch(query);
    }, 300);

    return () => window.clearTimeout(timeoutId);
  }, [onSearch, query]);

  return <p>Search query: {query}</p>;
}
~~~

**Why:** Without cleanup, every intermediate query eventually fires. Omitting
onSearch can also invoke an obsolete callback supplied by the parent. The
correct dependency list reflects every reactive value read by the effect; a
parent that needs a stable callback can provide one with useCallback.

**Verification note:** With fake timers, rerender rapidly with a, ab, and abc.
Advance 300 ms and assert one call with abc. Rerender with a new onSearch
function and assert that only the new function is called.

## 5. Unsubscribe from job events and append with current state

### Broken TSX

~~~tsx
import { useEffect, useState } from "react";

type JobEvent = {
  id: string;
  message: string;
};

type JobEvents = {
  subscribe: (
    jobId: string,
    listener: (event: JobEvent) => void,
  ) => () => void;
};

type JobEventFeedProps = {
  jobId: string;
  jobEvents: JobEvents;
};

export function JobEventFeed({ jobId, jobEvents }: JobEventFeedProps) {
  const [events, setEvents] = useState<JobEvent[]>([]);

  useEffect(() => {
    jobEvents.subscribe(jobId, (event) => {
      setEvents([...events, event]);
    });
  }, [jobId]);

  return (
    <ul>
      {events.map((event) => (
        <li key={event.id}>{event.message}</li>
      ))}
    </ul>
  );
}
~~~

### Corrected TSX

~~~tsx
import { useEffect, useState } from "react";

type JobEvent = {
  id: string;
  message: string;
};

type JobEvents = {
  subscribe: (
    jobId: string,
    listener: (event: JobEvent) => void,
  ) => () => void;
};

type JobEventFeedProps = {
  jobId: string;
  jobEvents: JobEvents;
};

export function JobEventFeed({ jobId, jobEvents }: JobEventFeedProps) {
  const [events, setEvents] = useState<JobEvent[]>([]);

  useEffect(() => {
    const unsubscribe = jobEvents.subscribe(jobId, (event) => {
      setEvents((current) => [...current, event]);
    });

    return unsubscribe;
  }, [jobEvents, jobId]);

  return (
    <ul>
      {events.map((event) => (
        <li key={event.id}>{event.message}</li>
      ))}
    </ul>
  );
}
~~~

**Why:** The broken listener captures the event array from the render that
created the subscription, so successive messages can replace one another. It
also leaks the subscription and ignores changes to the event source. A
functional update removes the stale read; returning unsubscribe and listing
both inputs gives the subscription the correct lifetime.

**Verification note:** Use a fake event source. Emit two events synchronously
and assert that both render. Change jobId and assert that the old subscription
is removed; unmount and assert that the final subscription is also removed.

## 6. Keep a cart callback stable without freezing cart state

### Broken TSX

~~~tsx
import { useCallback, useState } from "react";

type CartItem = {
  id: string;
  name: string;
};

export function CartButton() {
  const [items, setItems] = useState<CartItem[]>([]);

  const addItem = useCallback((item: CartItem) => {
    setItems([...items, item]);
  }, []);

  return (
    <button
      type="button"
      onClick={() =>
        addItem({ id: crypto.randomUUID(), name: "Notification credit" })
      }
    >
      Add item ({items.length})
    </button>
  );
}
~~~

### Corrected TSX

~~~tsx
import { useCallback, useState } from "react";

type CartItem = {
  id: string;
  name: string;
};

export function CartButton() {
  const [items, setItems] = useState<CartItem[]>([]);

  const addItem = useCallback((item: CartItem) => {
    setItems((current) => [...current, item]);
  }, []);

  return (
    <button
      type="button"
      onClick={() =>
        addItem({ id: crypto.randomUUID(), name: "Notification credit" })
      }
    >
      Add item ({items.length})
    </button>
  );
}
~~~

**Why:** The empty dependency list makes the callback permanently capture the
initial empty cart. Adding items to that snapshot loses earlier additions. The
functional updater expresses the next state from the current state, so the
callback can remain stable without depending on items.

**Verification note:** Click twice and expect the displayed count to be 2.
Also pass addItem to a memoized child in a test harness and confirm its identity
does not change after an addition.

## 7. Include every calculation input in useMemo

### Broken TSX

~~~tsx
import { useMemo } from "react";

type PriceSummaryProps = {
  subtotal: number;
  taxRate: number;
};

export function PriceSummary({
  subtotal,
  taxRate,
}: PriceSummaryProps) {
  const grandTotal = useMemo(
    () => subtotal * (1 + taxRate),
    [subtotal],
  );

  return <output>Total: {grandTotal.toFixed(2)}</output>;
}
~~~

### Corrected TSX

~~~tsx
import { useMemo } from "react";

type PriceSummaryProps = {
  subtotal: number;
  taxRate: number;
};

export function PriceSummary({
  subtotal,
  taxRate,
}: PriceSummaryProps) {
  const grandTotal = useMemo(
    () => subtotal * (1 + taxRate),
    [subtotal, taxRate],
  );

  return <output>Total: {grandTotal.toFixed(2)}</output>;
}
~~~

**Why:** Memoization is a performance optimization, not permission to omit an
input. With taxRate absent, changing only the tax rate returns a cached,
incorrect total. This calculation is cheap enough to perform directly in real
code, but if it is memoized its dependency list must be complete.

**Verification note:** Render a subtotal of 100 at 5% tax, then rerender at 10%
without changing the subtotal. Assert that the total changes from 105.00 to
110.00.

## 8. Compute filtered products instead of synchronizing derived state

### Broken TSX

~~~tsx
import { useEffect, useState } from "react";

type Product = {
  id: string;
  name: string;
};

type ProductListProps = {
  products: Product[];
  query: string;
};

export function ProductList({ products, query }: ProductListProps) {
  const [visibleProducts, setVisibleProducts] = useState<Product[]>([]);

  useEffect(() => {
    const normalizedQuery = query.trim().toLowerCase();
    setVisibleProducts(
      products.filter((product) =>
        product.name.toLowerCase().includes(normalizedQuery),
      ),
    );
  }, [products, query]);

  return (
    <ul>
      {visibleProducts.map((product) => (
        <li key={product.id}>{product.name}</li>
      ))}
    </ul>
  );
}
~~~

### Corrected TSX

~~~tsx
import { useMemo } from "react";

type Product = {
  id: string;
  name: string;
};

type ProductListProps = {
  products: Product[];
  query: string;
};

export function ProductList({ products, query }: ProductListProps) {
  const visibleProducts = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase();

    return products.filter((product) =>
      product.name.toLowerCase().includes(normalizedQuery),
    );
  }, [products, query]);

  return (
    <ul>
      {visibleProducts.map((product) => (
        <li key={product.id}>{product.name}</li>
      ))}
    </ul>
  );
}
~~~

**Why:** visibleProducts is fully determined by props. Storing it creates a
second source of truth, renders once with obsolete data, and then schedules
another render from the effect. Computing during render keeps the result in
sync. useMemo is optional here and only avoids repeating a potentially
expensive filter when its inputs are unchanged.

**Verification note:** Render a known product set, change only query, and assert
the matching list on the resulting render. In a profiler or render-count test,
confirm the correction does not perform an effect-driven follow-up commit.

## 9. Call hooks unconditionally and guard inside the effect

### Broken TSX

~~~tsx
import { useEffect, useState } from "react";

type ChatClient = {
  subscribe: (
    roomId: string,
    listener: (message: string) => void,
  ) => () => void;
};

type ChatRoomProps = {
  chat: ChatClient;
  enabled: boolean;
  roomId: string;
};

export function ChatRoom({ chat, enabled, roomId }: ChatRoomProps) {
  const [lastMessage, setLastMessage] = useState<string | null>(null);

  if (!enabled) {
    return <p>Chat disabled</p>;
  }

  useEffect(() => {
    return chat.subscribe(roomId, setLastMessage);
  }, [roomId]);

  return <p>{lastMessage ?? "Waiting for a message..."}</p>;
}
~~~

### Corrected TSX

~~~tsx
import { useEffect, useState } from "react";

type ChatClient = {
  subscribe: (
    roomId: string,
    listener: (message: string) => void,
  ) => () => void;
};

type ChatRoomProps = {
  chat: ChatClient;
  enabled: boolean;
  roomId: string;
};

export function ChatRoom({ chat, enabled, roomId }: ChatRoomProps) {
  const [lastMessage, setLastMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!enabled) {
      return;
    }

    return chat.subscribe(roomId, setLastMessage);
  }, [chat, enabled, roomId]);

  if (!enabled) {
    return <p>Chat disabled</p>;
  }

  return <p>{lastMessage ?? "Waiting for a message..."}</p>;
}
~~~

**Why:** The early return causes the component to call a different number of
hooks when enabled changes, violating the Rules of Hooks. Calling the effect on
every render preserves hook order. The internal guard controls the external
subscription, and the complete dependencies ensure it is replaced when its
source, status, or room changes.

**Verification note:** Rerender from disabled to enabled and back without a
hook-order error. Assert one subscription when enabled, its cleanup when
disabled, and no subscription while disabled.

## 10. Depend on stable scalar inputs instead of a new object

### Broken TSX

~~~tsx
import { useEffect } from "react";

type ConnectionOptions = {
  serverUrl: string;
  roomId: string;
};

type Connection = {
  connect: () => void;
  disconnect: () => void;
};

type CreateConnection = (options: ConnectionOptions) => Connection;

type ConnectedRoomProps = {
  serverUrl: string;
  roomId: string;
  theme: string;
  createConnection: CreateConnection;
};

export function ConnectedRoom({
  serverUrl,
  roomId,
  theme,
  createConnection,
}: ConnectedRoomProps) {
  const options = { serverUrl, roomId };

  useEffect(() => {
    const connection = createConnection(options);
    connection.connect();

    return () => connection.disconnect();
  }, [createConnection, options]);

  return <section className={theme}>Connected to {roomId}</section>;
}
~~~

### Corrected TSX

~~~tsx
import { useEffect } from "react";

type ConnectionOptions = {
  serverUrl: string;
  roomId: string;
};

type Connection = {
  connect: () => void;
  disconnect: () => void;
};

type CreateConnection = (options: ConnectionOptions) => Connection;

type ConnectedRoomProps = {
  serverUrl: string;
  roomId: string;
  theme: string;
  createConnection: CreateConnection;
};

export function ConnectedRoom({
  serverUrl,
  roomId,
  theme,
  createConnection,
}: ConnectedRoomProps) {
  useEffect(() => {
    const connection = createConnection({ serverUrl, roomId });
    connection.connect();

    return () => connection.disconnect();
  }, [createConnection, roomId, serverUrl]);

  return <section className={theme}>Connected to {roomId}</section>;
}
~~~

**Why:** The options object in the broken component is new on every render, so
React disconnects and reconnects even when only an unrelated prop such as theme
changes. Constructing the object inside the effect lets the dependency list
describe the primitive values that actually control the connection.

**Verification note:** Use a fake connection factory and rerender with only a
new theme. Assert no additional connect or disconnect. Then change roomId and
assert exactly one disconnect followed by one new connection; unmount and
assert final cleanup.

## Submission boundary

This file is ready to paste into a GitHub Gist after an authorized person
reviews the substitute status and publishes it. Publication and EM feedback are
tracked separately in [react-gist-link.md](react-gist-link.md).
