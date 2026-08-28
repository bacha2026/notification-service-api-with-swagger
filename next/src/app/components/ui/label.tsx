import type { LabelHTMLAttributes } from "react";
import { cn } from "@/app/lib/utils";

export function Label({ className, ...props }: LabelHTMLAttributes<HTMLLabelElement>) {
  return <label className={cn("text-sm font-medium leading-none text-[#384237]", className)} {...props} />;
}
