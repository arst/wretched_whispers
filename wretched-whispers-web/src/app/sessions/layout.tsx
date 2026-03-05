"use client";

import Header from "@/components/layout/Header";
import AuthGuard from "@/components/layout/AuthGuard";

export default function SessionsLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <AuthGuard>
      <Header />
      {children}
    </AuthGuard>
  );
}
