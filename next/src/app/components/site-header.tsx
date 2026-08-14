"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState } from "react";
import { useStore } from "@/app/components/store-provider";

const navItems = [
  { href: "/", label: "Home" },
  { href: "/cart", label: "Cart" },
  { href: "/order", label: "Order" },
  { href: "/about-us", label: "About Us" },
  { href: "/contact-us", label: "Contact Us" },
];

function ChevronDownIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="m5.5 7.5 4.5 4.5 4.5-4.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

function UserIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.65">
      <circle cx="12" cy="8" r="3.2" />
      <path d="M5.5 20c.9-3.1 3.1-4.65 6.5-4.65S17.6 16.9 18.5 20" strokeLinecap="round" />
    </svg>
  );
}

function BagIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M5.25 8.75h13.5l-.8 10.25H6.05L5.25 8.75Z" strokeLinejoin="round" />
      <path d="M8.75 9V7a3.25 3.25 0 0 1 6.5 0v2" strokeLinecap="round" />
    </svg>
  );
}

export function SiteHeader() {
  const pathname = usePathname();
  const [menuOpen, setMenuOpen] = useState(false);
  const { cartItemCount } = useStore();

  return (
    <>
      <header className="site-header">
        <Link className="brand" href="/" aria-label="Urban Grit home">
          Urban <span>Grit</span>
        </Link>

        <div className="header-actions">
          <Link className="header-cart-link" href="/cart" aria-label={`View cart with ${cartItemCount} items`}>
            <BagIcon />
            {cartItemCount > 0 && <span className="cart-count">{cartItemCount}</span>}
          </Link>

          <div className="profile-menu">
            <div className="visitor-avatar" role="img" aria-label="Visitor profile picture">
              <UserIcon />
            </div>
            <button
              className="profile-toggle"
              type="button"
              aria-label="Open visitor menu"
              aria-expanded={menuOpen}
              onClick={() => setMenuOpen((open) => !open)}
            >
              <ChevronDownIcon />
            </button>

            {menuOpen && (
              <div className="profile-dropdown" role="menu">
                <button type="button" role="menuitem" onClick={() => setMenuOpen(false)}>
                  Settings
                </button>
                <button type="button" role="menuitem" onClick={() => setMenuOpen(false)}>
                  Logout
                </button>
              </div>
            )}
          </div>
        </div>
      </header>

      <nav className="main-nav" aria-label="Main navigation">
        <div className="main-nav-inner">
          {navItems.map((item) => {
            const isActive = item.href === "/" ? pathname === "/" : pathname.startsWith(item.href);

            return (
              <Link
                key={item.href}
                className={isActive ? "nav-link nav-link-active" : "nav-link"}
                href={item.href}
              >
                {item.label}
              </Link>
            );
          })}
        </div>
      </nav>
    </>
  );
}
