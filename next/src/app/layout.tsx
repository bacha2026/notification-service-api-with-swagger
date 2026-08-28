import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import { SiteHeader } from "@/app/components/site-header";
import { QueryProvider } from "@/app/components/query-provider";
import { StoreProvider } from "@/app/components/store-provider";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Urban Grit | Everyday city essentials",
  description: "Browse thoughtful food and pantry essentials from Urban Grit.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="en"
      className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}
    >
      <body className="min-h-full">
        <QueryProvider>
          <StoreProvider>
            <SiteHeader />
            <main className="site-main">{children}</main>
          </StoreProvider>
        </QueryProvider>
      </body>
    </html>
  );
}
