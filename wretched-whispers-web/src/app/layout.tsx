import type { Metadata } from "next";
import { Inter, UnifrakturMaguntia } from "next/font/google";
import "./globals.css";

const inter = Inter({
  subsets: ["latin"],
  variable: "--font-inter",
});

const doomDisplay = UnifrakturMaguntia({
  weight: "400",
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
        {children}
      </body>
    </html>
  );
}
