import type { InputHTMLAttributes } from "react";
import { cn } from "@/app/lib/utils";

export function Input({ className, type, ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      className={cn(
        "flex h-10 w-full rounded-md border border-[#d5d3ca] bg-[#fffdfa] px-3 py-2 text-sm text-[#1d2420] shadow-sm transition-colors placeholder:text-[#8a9087] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#98bf6c] focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50",
        className,
      )}
      type={type}
      {...props}
    />
  );
}
