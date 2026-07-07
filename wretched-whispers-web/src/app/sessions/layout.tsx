"use client";

import Header from "@/components/layout/Header";
import AuthGuard from "@/components/layout/AuthGuard";
import DesktopSettingsGate from "@/components/layout/DesktopSettingsGate";

export default function SessionsLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <AuthGuard>
      <DesktopSettingsGate>
        <Header />
        {children}
      </DesktopSettingsGate>
    </AuthGuard>
  );
}
