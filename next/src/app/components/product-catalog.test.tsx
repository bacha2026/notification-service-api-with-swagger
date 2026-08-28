import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ProductCatalog } from "@/app/components/product-catalog";

const { mockUseStore } = vi.hoisted(() => ({
  mockUseStore: vi.fn(),
}));

vi.mock("@/app/components/store-provider", () => ({
  useStore: mockUseStore,
}));

describe("ProductCatalog", () => {
  it("clears the transient Added timeout when the component unmounts", async () => {
    const addToCart = vi.fn().mockResolvedValue(true);
    mockUseStore.mockReturnValue({
      addToCart,
      isCartMutating: false,
      products: [
        {
          id: 7,
          name: "Coffee beans",
          shortDescription: "Freshly roasted",
          description: "A test product",
          price: 450,
          quantityAvailable: 3,
          imageUrl: "/coffee.jpg",
        },
      ],
      productsError: null,
      productsState: "ready",
      refreshProducts: vi.fn(),
    });
    const setTimeoutSpy = vi.spyOn(window, "setTimeout");
    const clearTimeoutSpy = vi.spyOn(window, "clearTimeout");
    const view = render(<ProductCatalog />);

    fireEvent.click(screen.getByRole("button", { name: "Add to cart" }));

    expect(await screen.findByRole("button", { name: "Added" })).toBeInTheDocument();
    expect(addToCart).toHaveBeenCalledWith(7);

    await waitFor(() => {
      expect(setTimeoutSpy.mock.calls.some(([, delay]) => delay === 1800)).toBe(true);
    });
    const timeoutCallIndex = setTimeoutSpy.mock.calls.findIndex(([, delay]) => delay === 1800);
    expect(timeoutCallIndex).toBeGreaterThanOrEqual(0);
    const timeoutId = setTimeoutSpy.mock.results[timeoutCallIndex]?.value;

    view.unmount();

    expect(clearTimeoutSpy).toHaveBeenCalledWith(timeoutId);
  });
});
