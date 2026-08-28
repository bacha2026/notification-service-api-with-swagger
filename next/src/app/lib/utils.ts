import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

/** Combines Tailwind utility classes the same way shadcn/ui components do. */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}
