"use client";

import { useMemo, useState } from "react";
import { useStore } from "@/app/components/store-provider";
import type { Product } from "@/app/lib/store-types";

type ProductCategory = "All products" | "Coffee & pantry" | "Bakery" | "Fresh picks" | "Meals";

const categories: ProductCategory[] = [
  "All products",
  "Coffee & pantry",
  "Bakery",
  "Fresh picks",
  "Meals",
];

function categoryForProduct(product: Product): ProductCategory {
  const name = product.name.toLowerCase();

  if (name.includes("coffee")) return "Coffee & pantry";
  if (name.includes("croissant")) return "Bakery";
  if (name.includes("berry")) return "Fresh picks";
  return "Meals";
}

function formatPrice(price: number) {
  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
    maximumFractionDigits: 0,
  }).format(price);
}

function SearchIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
      <circle cx="10.75" cy="10.75" r="5.75" />
      <path d="m15.15 15.15 4.1 4.1" strokeLinecap="round" />
    </svg>
  );
}

function ArrowRightIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M5 12h13.5M14 6.5l5.5 5.5-5.5 5.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

export function ProductCatalog() {
  const [category, setCategory] = useState<ProductCategory>("All products");
  const [search, setSearch] = useState("");
  const [addedProductId, setAddedProductId] = useState<number | null>(null);
  const { addToCart, isCartMutating, products, productsError, productsState, refreshProducts } = useStore();

  const filteredProducts = useMemo(() => {
    const normalizedSearch = search.trim().toLowerCase();

    return products.filter((product) => {
      const matchesCategory = category === "All products" || categoryForProduct(product) === category;
      const searchText = `${product.name} ${product.shortDescription} ${product.description}`.toLowerCase();
      const matchesSearch = !normalizedSearch || searchText.includes(normalizedSearch);

      return matchesCategory && matchesSearch;
    });
  }, [category, products, search]);

  async function handleAddToCart(productId: number) {
    const added = await addToCart(productId);

    if (added) {
      setAddedProductId(productId);
      window.setTimeout(() => setAddedProductId(null), 1800);
    }
  }

  return (
    <section className="catalog-section" aria-labelledby="catalog-heading">
      <div className="catalog-intro">
        <div>
          <p className="eyebrow">Curated for everyday city living</p>
          <h1 id="catalog-heading">Find your next good thing.</h1>
        </div>
        <p className="catalog-source">Live product details from Notification API</p>
      </div>

      <div className="filtering-section">
        <label className="filter-select-label">
          <span>Filter products</span>
          <select value={category} onChange={(event) => setCategory(event.target.value as ProductCategory)}>
            {categories.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </label>

        <label className="search-field">
          <SearchIcon />
          <span className="sr-only">Search products</span>
          <input
            type="search"
            value={search}
            placeholder="Search products"
            onChange={(event) => setSearch(event.target.value)}
          />
        </label>
      </div>

      {productsState === "loading" && (
        <div className="catalog-status" role="status">
          Loading the latest Urban Grit products…
        </div>
      )}

      {productsState === "error" && (
        <div className="catalog-status catalog-status-error" role="alert">
          <p>We couldn’t reach the product catalog. {productsError}</p>
          <button className="text-button" type="button" onClick={() => void refreshProducts()}>
            Try again <ArrowRightIcon />
          </button>
        </div>
      )}

      {productsState === "ready" && filteredProducts.length === 0 && (
        <div className="catalog-status">
          No products match that filter. Try another category or search term.
        </div>
      )}

      {productsState === "ready" && filteredProducts.length > 0 && (
        <div className="product-grid">
          {filteredProducts.map((product) => {
            const soldOut = product.quantityAvailable < 1;
            const added = addedProductId === product.id;

            return (
              <article className="product-card" key={product.id}>
                <div
                  className="product-image"
                  role="img"
                  aria-label={product.name}
                  style={{ backgroundImage: `url("${product.imageUrl}")` }}
                >
                  <span className="product-category">{categoryForProduct(product)}</span>
                </div>
                <div className="product-content">
                  <div>
                    <h2>{product.name}</h2>
                    <p>{product.shortDescription}</p>
                  </div>
                  <div className="product-card-footer">
                    <strong>{formatPrice(product.price)}</strong>
                    <button
                      className={added ? "add-button add-button-success" : "add-button"}
                      type="button"
                      disabled={soldOut || isCartMutating}
                      onClick={() => void handleAddToCart(product.id)}
                    >
                      {soldOut ? "Sold out" : added ? "Added" : "Add to cart"}
                    </button>
                  </div>
                </div>
              </article>
            );
          })}
        </div>
      )}
    </section>
  );
}
