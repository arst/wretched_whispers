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
      <div className="max-w-2xl mx-auto px-4 pt-20 pb-8">
        {children}
      </div>
    </AuthGuard>
  );
}
