"use client";

import Link from "next/link";
import { useQueryClient } from "@tanstack/react-query";
import { ChevronDown, LogOut, PackageOpen, UserRound } from "lucide-react";
import { usePathname } from "next/navigation";
import { LoginDialog } from "@/app/components/auth/login-dialog";
import { useCurrentUser } from "@/app/components/auth/use-auth";
import { NotificationBell } from "@/app/components/notifications/notification-bell";
import { Button } from "@/app/components/ui/button";
import { Avatar, AvatarFallback, AvatarImage } from "@/app/components/ui/avatar";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/app/components/ui/dropdown-menu";
import { useStore } from "@/app/components/store-provider";
import { useAuthStore } from "@/app/lib/auth-store";

const navItems = [
  { href: "/", label: "Home" },
  { href: "/cart", label: "Cart" },
  { href: "/order", label: "Order" },
  { href: "/about-us", label: "About Us" },
  { href: "/contact-us", label: "Contact Us" },
];

function BagIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M5.25 8.75h13.5l-.8 10.25H6.05L5.25 8.75Z" strokeLinejoin="round" />
      <path d="M8.75 9V7a3.25 3.25 0 0 1 6.5 0v2" strokeLinecap="round" />
    </svg>
  );
}

function initials(firstName: string, lastName: string) {
  return `${firstName.slice(0, 1)}${lastName.slice(0, 1)}`.toUpperCase() || "U";
}

export function SiteHeader() {
  const pathname = usePathname();
  const queryClient = useQueryClient();
  const { cartItemCount, visitorEmail } = useStore();
  const session = useAuthStore((state) => state.session);
  const clearSession = useAuthStore((state) => state.clearSession);
  const setLoginDialogOpen = useAuthStore((state) => state.setLoginDialogOpen);
  useCurrentUser();

  const user = session?.user;
  const displayName = user ? `${user.firstName} ${user.lastName}`.trim() || user.email : "";

  function logOut() {
    queryClient.removeQueries({ queryKey: ["auth"] });
    clearSession();
  }

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

          {user ? (
            <>
              <NotificationBell visitorEmail={visitorEmail} />
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button
                    variant="ghost"
                    className="h-10 gap-2 rounded-full px-1.5 pr-2 text-[#f8f6ef] hover:bg-white/10 hover:text-white"
                    aria-label={`Open ${displayName}'s account menu`}
                  >
                    <Avatar className="size-8 border border-white/35">
                      {user.photoUrl && <AvatarImage src={user.photoUrl} alt={`${displayName}'s profile`} referrerPolicy="no-referrer" />}
                      <AvatarFallback>{initials(user.firstName, user.lastName)}</AvatarFallback>
                    </Avatar>
                    <span className="hidden max-w-36 truncate text-sm font-semibold sm:inline">{displayName}</span>
                    <ChevronDown className="size-4" aria-hidden="true" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <div className="border-b border-[#e8e5dc] px-2 py-2">
                    <p className="truncate text-sm font-semibold">{displayName}</p>
                    <p className="truncate text-xs text-[#69716a]">{user.email}</p>
                  </div>
                  <DropdownMenuItem asChild>
                    <Link href="/order"><PackageOpen className="mr-2 size-4" />My orders</Link>
                  </DropdownMenuItem>
                  <DropdownMenuItem asChild>
                    <Link href="/order"><UserRound className="mr-2 size-4" />Account</Link>
                  </DropdownMenuItem>
                  <DropdownMenuItem className="text-[#8b3d37] focus:bg-red-50 focus:text-[#8b3d37]" onSelect={logOut}>
                    <LogOut className="mr-2 size-4" />Log out
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </>
          ) : (
            <>
            <NotificationBell visitorEmail={visitorEmail} />
            <Button
              variant="ghost"
              className="h-9 rounded-md px-3 text-sm font-semibold text-[#f8f6ef] hover:bg-white/10 hover:text-white"
              onClick={() => setLoginDialogOpen(true)}
            >
              Login
            </Button>
            </>
          )}
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

      {pathname !== "/signup" && <LoginDialog />}
    </>
  );
}
