import type { Metadata } from "next";
import { Inter, Cinzel } from "next/font/google";
import StoreHydration from "@/components/providers/StoreHydration";
import "./globals.css";

const inter = Inter({
  subsets: ["latin"],
  variable: "--font-inter",
});

const doomDisplay = Cinzel({
  weight: ["400", "700", "900"],
  subsets: ["latin"],
  variable: "--font-doom-display",
  display: "swap",
});

export const metadata: Metadata = {
  title: "Wretched Whispers",
  description: "A doom-metal TTRPG experience",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en" className={`${inter.variable} ${doomDisplay.variable}`}>
      <body className="bg-doom-black text-doom-bone font-body min-h-screen antialiased">
        <StoreHydration />
        {children}
      </body>
    </html>
  );
}
