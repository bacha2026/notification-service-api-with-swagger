import type { SelectHTMLAttributes } from "react";
import { cn } from "@/app/lib/utils";

/** A compact, shadcn-styled native select with reliable mobile behavior. */
export function Select({ className, children, ...props }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select
      className={cn(
        "flex h-10 w-full rounded-md border border-[#d5d3ca] bg-[#fffdfa] px-3 py-2 text-sm text-[#1d2420] shadow-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#98bf6c] focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50",
        className,
      )}
      {...props}
    >
      {children}
    </select>
  );
}
